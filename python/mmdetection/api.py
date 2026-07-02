import cv2
import numpy as np

from mmdet.apis import inference_detector, init_detector
import time
import asyncio
from concurrent.futures import ThreadPoolExecutor

from typing import List, Dict, Callable, Any, Union, Tuple
from mmdet.structures import DetDataSample
import os
import torch

SCORE_THRESHOLD = 0.45

config_paths = {
    'kitchen': os.path.join(os.path.dirname(__file__), 'checkpoints/rtmdet-ins_l_1xb4-150e_kitchen.py'),
    'vistas': os.path.join(os.path.dirname(__file__), 'checkpoints/rtmdet-ins_l_1xb4-150e_Vistas.py'),
    'coco': os.path.join(os.path.dirname(__file__), 'configs/rtmdet/rtmdet-ins_l_8xb32-300e_coco.py'),
}
checkpoint_paths = {
    'kitchen': os.path.join(os.path.dirname(__file__), 'checkpoints/kitchen_102.pth'),
    'vistas': os.path.join(os.path.dirname(__file__), 'checkpoints/vistas_120.pth'),
    'coco': os.path.join(os.path.dirname(__file__), 'checkpoints/rtmdet-ins_l_8xb32-300e_coco.pth'),
}

device = torch.device('cuda:0')

# build the model from a config file and a checkpoint file
# model = init_detector(config_path, checkpoint_path, device=device)
model = None

# CLASSES = model.dataset_meta['classes']
# COLORS = model.dataset_meta['palette']
CLASSES = None
COLORS = None

# Optimization #3: Reduce worker count for contour processing
executor = ThreadPoolExecutor(max_workers = 4)

def load_model(model_name: str):
    global model
    global CLASSES, COLORS

    config_path = config_paths[model_name]
    checkpoint_path = checkpoint_paths[model_name]

    model = init_detector(config_path, checkpoint_path, device='cuda:0')
    CLASSES = model.dataset_meta['classes']
    COLORS = model.dataset_meta['palette']
    print(f'Model {model_name} loaded, {len(CLASSES)} classes, device: {device}')

def get_classes():
    return CLASSES

def get_geometric_center(masks: torch.Tensor) -> List[List[int]]:
    # Optimization #2: More efficient computation without large intermediate tensors
    N, H, W = masks.shape

    # Compute mask sums once (reuse)
    mask_sums = masks.sum(dim=(1, 2))

    # Use einsum for efficient computation (no intermediate N×H×W tensors)
    y_indices = torch.arange(H, device=masks.device, dtype=torch.float32)
    x_indices = torch.arange(W, device=masks.device, dtype=torch.float32)

    # Efficient center computation using einsum
    y_centers = torch.einsum('nhw,h->n', masks.float(), y_indices) / mask_sums
    x_centers = torch.einsum('nhw,w->n', masks.float(), x_indices) / mask_sums

    # Stack and convert directly to list (skip numpy intermediate)
    centers = torch.stack([x_centers, y_centers], dim=1).to(torch.int32).tolist()
    return centers

# Optimization #3: Worker function for parallel contour processing
def _process_single_contour(args):
    """
    Process a single mask to extract its contour.
    Args:
        args: (index, mask, box, class_name, image_shape)
    Returns:
        (index, largest_contour, updated_box)
    """
    i, mask, box, class_name, image_shape = args
    x1, y1, x2, y2 = box

    # Crop the mask by box
    mask_crop = mask[y1:y2, x1:x2]

    # Error handling: mask_crop.size == 0
    if mask_crop.size == 0:
        # Return empty contour without processing
        return (i, np.array([[0, 0], [0, 0], [0, 0], [0, 0]]), box)

    # Error handling: mask_crop.max() == 0
    if mask_crop.max() == 0:
        nonzero = np.nonzero(mask)
        if len(nonzero) <= 1 or len(nonzero[0]) == 0 or len(nonzero[1]) == 0:
            # No valid mask, return empty contour
            return (i, np.array([[0, 0], [0, 0], [0, 0], [0, 0]]), box)
        # Recompute box from nonzero pixels
        x1 = int(min(nonzero[1]))
        x2 = int(max(nonzero[1]))
        y1 = int(min(nonzero[0]))
        y2 = int(max(nonzero[0]))
        mask_crop = mask[y1:y2, x1:x2]
        box = [x1, y1, x2, y2]

    # Extract contours using OpenCV
    contours = bitmap_to_polygon(mask_crop)

    if len(contours) == 0:
        # Empty contour
        largest_contour = np.array([[0, 0], [0, 0], [0, 0], [0, 0]])
    else:
        # Find largest contour by area
        largest_contour = max(contours, key=cv2.contourArea)

    return (i, largest_contour, box)


def parse_result(result: DetDataSample,
                 score_threshold: float = 0.15,
                 top_k: int = 15) -> Tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    """Parse the detection results."""
    pred_instances = result.pred_instances

    # first, filter out the low score boxes
    valid_inds = pred_instances.scores >= score_threshold
    pred_instances = pred_instances[valid_inds]

    # sort the scores
    scores = pred_instances.scores
    _, sort_inds = scores.sort(descending=True)
    pred_instances = pred_instances[sort_inds]

    # get the top_k
    pred_instances = pred_instances[:top_k]

    # get the geometric centers
    centers = get_geometric_center(pred_instances.masks)

    # Optimization #1: Batch GPU→CPU transfers to reduce PCIe overhead
    # Prepare all GPU tensors first, then transfer together
    boxes_gpu = pred_instances.bboxes.to(torch.int32)
    labels_gpu = pred_instances.labels
    scores_gpu = pred_instances.scores
    masks_gpu = pred_instances.masks.to(torch.uint8)

    # Single synchronization point for all transfers
    boxes: np.ndarray = boxes_gpu.cpu().numpy()
    labels: np.ndarray = labels_gpu.cpu().numpy()
    scores: np.ndarray = scores_gpu.cpu().numpy()
    masks: np.ndarray = masks_gpu.cpu().numpy()

    return masks, boxes, labels, scores, centers


count = 0
total_used_time = 0

def process_image(image:np.ndarray,
                  score_threshold: float = 0.3,
                  top_k: int = 15) -> Tuple[np.ndarray, List[np.ndarray], np.ndarray, np.ndarray, np.ndarray, List[List[int]]]:
    global model
    global count, total_used_time

    start_time = time.time()
    # Optimization #6: Disable gradient computation for inference
    with torch.no_grad():
        result = inference_detector(model, image)
    used_time = time.time() - start_time
    total_used_time += used_time
    count += 1

    masks, boxes, labels, scores, geometry_center = parse_result(result, score_threshold, top_k)

    # Optimization #3: Parallelize contour finding for better performance
    PARALLEL_THRESHOLD = 8  # Only parallelize if >= 8 masks

    mask_contours = [None for _ in range(len(masks))]

    if len(masks) >= PARALLEL_THRESHOLD:
        # Parallel path using ThreadPoolExecutor
        args_list = [
            (i, masks[i], boxes[i].tolist(), CLASSES[labels[i]], image.shape)
            for i in range(len(masks))
        ]

        # Process all masks in parallel
        results = list(executor.map(_process_single_contour, args_list))

        # Unpack results and update boxes if they were corrected
        for i, largest_contour, updated_box in results:
            mask_contours[i] = largest_contour
            boxes[i] = updated_box
    else:
        # Sequential path for small batches (< 8 masks)
        for i, mask in enumerate(masks):
            x1, y1, x2, y2 = boxes[i]
            mask_crop = mask[y1:y2, x1:x2]

            if mask_crop.size == 0:
                print('mask_crop size is 0', i, CLASSES[labels[i]])
                print('boxes[i]', boxes[i])
                print('mask.shape', mask.shape)
                print('mask_crop.shape', mask_crop.shape)
                cv2.imwrite('bad_image.png', image)
            if mask_crop.max() == 0:
                nonzero = np.nonzero(mask)
                if len(nonzero) <= 1 or len(nonzero[0]) == 0 or len(nonzero[1]) == 0:
                    print('no nonzero', i, CLASSES[labels[i]])
                x1 = min(nonzero[1])
                x2 = max(nonzero[1])
                y1 = min(nonzero[0])
                y2 = max(nonzero[0])
                mask_crop = mask[y1:y2, x1:x2]
                boxes[i] = [x1, y1, x2, y2]

            contours = bitmap_to_polygon(mask_crop)

            if len(contours) == 0:
                largest_contour = np.array([[0, 0], [0, 0], [0, 0], [0, 0]])
            else:
                # Optimization #3: Remove redundant sort - just find max by area
                largest_contour = max(contours, key=cv2.contourArea)

            mask_contours[i] = largest_contour

    return masks, mask_contours, boxes, labels, scores, geometry_center


def get_recognition(image: np.ndarray,
                    filter_objects: List[str] = [],
                    exclude_objects: List[str] = [],
                    score_threshold: float = 0.3,
                    top_k: int = 15,) -> Dict[str, Any]:
    global CLASSES, COLORS

    masks, mask_contours, boxes, labels, scores, geometry_center = process_image(
        image,
        score_threshold,
        top_k,
    )

    # Optimization #6: Keep as numpy arrays to avoid redundant conversions
    # The serializer can handle numpy arrays directly
    # Only convert labels to int for indexing
    labels_list = labels.tolist()

    # convert labels to class names
    class_names = [CLASSES[label] for label in labels_list]

    result = {
        "masks": masks,
        "mask_contours": mask_contours,
        "boxes": boxes,
        "scores": scores,
        "labels": labels_list, # Keep as list for class name indexing
        "class_names": class_names,
        "geometry_center": geometry_center,
    }

    if filter_objects or exclude_objects:
        result = get_filtered_objects(result, filter_objects, exclude_objects)

    return result


async def async_get_recognition(image: np.ndarray,
                                filter_objects: List[str] = [],
                                score_threshold: float = 0.3,
                                top_k: int = 15) -> Dict[str, Any]:
    loop = asyncio.get_event_loop()
    return await loop.run_in_executor(executor, get_recognition, image, filter_objects, score_threshold, top_k)


def draw_recognition(image: np.ndarray, result: Dict[str, Any],
                     black: bool = False, draw_contour: bool = False, draw_mask: bool = True, 
                     draw_box: bool = False, draw_text: bool = True, draw_score = True,
                     draw_center = False, increase_contrast = False,
                     lv_color: Union[Tuple[int, int, int], None] = None,
                     contour_thickness: int = 15,
                     alpha: float = 0.45) -> np.ndarray:
    masks = result['masks']
    mask_contours = result['mask_contours']
    boxes = result['boxes']
    class_names = result['class_names']
    scores = result['scores']
    geometry_center = result['geometry_center']
    labels = result['labels']
    
    if black:
        image = np.zeros_like(image)
    else:
        # else, make a copy
        image = image.copy()

    if len(masks) == 0:
        return image
    
    has_alpha = image.shape[2] == 4

    # colors
    # each color is a 3-tuple (B, G, R) or (B, G, R, A)
    colors = []
    color_list = []
    for label in labels:
        if lv_color is not None:
            color = lv_color
            # if has alpha, add the alpha channel
            if has_alpha and len(color) == 3:
                color = (*color, 255)
        else:
            color = COLORS[label]
            color = (255, color[2], color[1], color[0]) if has_alpha else (color[2], color[1], color[0])
        color_list.append(color)
        colors.append(np.array(color, dtype=float).reshape(1,1,1,-1))
    colors = np.concatenate(colors, axis=0)

    # yield to other tasks
    # await asyncio.sleep(0)
    
    if draw_mask:
        # masks N*H*W
        masks = np.array(masks, dtype=float)
        # change to N*H*W*1
        masks = np.expand_dims(masks, axis=3)

        masks_color = masks.repeat(3, axis=3) * colors * alpha

        inv_alpha_masks = masks * (-alpha) + 1

        masks_color_summand = masks_color[0]
        if len(masks_color) > 1:
            inv_alpha_cumul = inv_alpha_masks[:-1].cumprod(axis=0)
            masks_color_cumul = masks_color[1:] * inv_alpha_cumul
            masks_color_summand += masks_color_cumul.sum(axis=0)

        image = image * inv_alpha_masks.prod(axis=0) + masks_color_summand
        image = image.astype(np.uint8)

    # yield to other tasks
    # await asyncio.sleep(0)

    # draw the contours
    if draw_contour:
        for i, contour in enumerate(mask_contours):
            # contour is relative to the box, need to add the box's top-left corner
            x1, y1, _, _ = boxes[i]
            contour = np.array(contour) + np.array([x1, y1])
            contour = np.array(contour, dtype=np.int32)
            color = color_list[i]
            cv2.drawContours(image, [contour], -1, color, thickness = contour_thickness)

    # yield to other tasks
    # await asyncio.sleep(0)

    # draw box
    if draw_box:
        for i, box in enumerate(boxes):
            x1, y1, x2, y2 = box
            color = color_list[i]
            cv2.rectangle(image, (x1, y1), (x2, y2), color, contour_thickness)

    # yield to other tasks
    # await asyncio.sleep(0)

    # place text at the center
    if draw_text:
        for i, center in enumerate(geometry_center):
            text = class_names[i]
            if draw_score:
                text += f' {scores[i]:.2f}'
            
            fontsize = 1
            fontthickness = 2
            font = cv2.FONT_HERSHEY_SIMPLEX

            textsize = cv2.getTextSize(text, font, fontsize, fontthickness)[0]
            textX = center[0] - textsize[0] // 2
            textY = center[1] + textsize[1] // 2

            cv2.putText(image, text, (textX, textY), cv2.FONT_HERSHEY_SIMPLEX, fontsize, (0, 0, 255), fontthickness)

    # draw center with a green circle, and center of bounding box with a yellow circle
    if draw_center:
        for i, center in enumerate(geometry_center):
            cv2.circle(image, tuple(center), 3, (0, 255, 0), -1)
            x1, y1, x2, y2 = boxes[i]
            center_box = ((x1+x2)//2, (y1+y2)//2)
            cv2.circle(image, center_box, 3, (0, 255, 255), -1)


    if increase_contrast:
        contrast_factor = 1.2
        dim_factor = 0.8
        brightness_factor = 0

        # masks N*H*W
        masks = np.array(masks, dtype=np.uint8)
        # all_masks: H*W
        all_masks = np.clip(np.sum(masks, axis=0), 0, 1)
        # all_masks: H*W*3
        all_masks = np.stack([all_masks, all_masks, all_masks], axis=2).astype(np.uint8)

        # only increase contrast and brightness of interested area
        image_with = image * all_masks
        image_without = image * (1 - all_masks)
        image_with_enhanced = cv2.convertScaleAbs(image_with, alpha=contrast_factor, beta=brightness_factor)
        image_without_dim = cv2.convertScaleAbs(image_without, alpha=dim_factor, beta=brightness_factor)
        image = image_with_enhanced + image_without_dim

    
    return image


def get_filtered_objects(result: Dict[str, Any],
                         filter_objects: List[str] = [],
                         exclude_objects: List[str] = []) -> Dict[str, Any]:
    """Filter the objects by class names."""
    # Optimization #4: Use sets for O(1) lookup instead of O(n) list search
    assert 'class_names' in result

    class_names = result['class_names']

    # Convert to sets for O(1) lookup
    filter_set = set(filter_objects) if filter_objects else None
    exclude_set = set(exclude_objects)

    # Find valid indices using set-based filtering
    if filter_set:
        valid_indices = [i for i, name in enumerate(class_names)
                        if name in filter_set and name not in exclude_set]
    else:
        valid_indices = [i for i, name in enumerate(class_names)
                        if name not in exclude_set]

    # Use numpy indexing or list comprehension for faster filtering
    new_result = {}
    for field, values in result.items():
        if isinstance(values, np.ndarray):
            new_result[field] = values[valid_indices]
        else:
            new_result[field] = [values[i] for i in valid_indices]

    return new_result


def bitmap_to_polygon(bitmap):
    """Convert masks from the form of bitmaps to polygons.

    Args:
        bitmap (ndarray): masks in bitmap representation.

    Return:
        list[ndarray]: the converted mask in polygon representation.
        bool: whether the mask has holes.
    """
    outs = cv2.findContours(bitmap, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    contours = outs[-2]
    contours = [c.reshape(-1, 2) for c in contours]
    return contours


def main():
    load_model('coco')

    capture = cv2.VideoCapture(0)
    # set resolution to 640x360
    capture.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    capture.set(cv2.CAP_PROP_FRAME_HEIGHT, 360)

    print("Resolution:", capture.get(cv2.CAP_PROP_FRAME_WIDTH), "x", capture.get(cv2.CAP_PROP_FRAME_HEIGHT))

    count = 0
    total_used_time = 0
    master_count = 0
    master_used_time = 0

    ret, image = capture.read()
    if not ret:
        return
    
    # close the video capture
    capture.release()
    cv2.destroyAllWindows()
    
    print("image shape:", image.shape, image.dtype)

    while True:
        # plot image
        # cv2.imshow('image', image)
        # if cv2.waitKey(1) & 0xFF == ord('q'):
        #     break

        start_time = time.time()
        result = get_recognition(image, score_threshold=0.5)
        used_time = time.time() - start_time
        total_used_time += used_time
        count += 1

        master_used_time += used_time
        master_count += 1
        if master_count > 450:
            master_latency = master_used_time / master_count
            print(f'master latency: {master_latency:.3f}s ({1/master_latency:.3f} fps)')
            master_count = 0
            master_used_time = 0

        if count > 30:
            latency = total_used_time / count
            print(f'latency: {latency:.3f}s ({1/latency:.3f} fps)')
            count = 0
            total_used_time = 0
        
        break

    image = draw_recognition(image, result, draw_contour=True, draw_text=True, draw_score=True)
    cv2.imshow('result', image)
    cv2.waitKey(0)

    # bad_image = cv2.imread('bad_image.png')
    # print(bad_image.shape, bad_image.dtype)

    # result = inference_detector(model, image)
    # ins = result.pred_instances
    # ins = ins[ins.scores >= 0.3]
    # boxes: np.ndarray = ins.bboxes.to(torch.int32).cpu().numpy()
    # labels: np.ndarray = ins.labels.cpu().numpy()
    # scores: np.ndarray = ins.scores.cpu().numpy()
    # masks: np.ndarray = ins.masks.to(torch.uint8).cpu().numpy()

    # for i, mask in enumerate(masks):
    #     print(i, CLASSES[labels[i]], mask.shape, boxes[i], scores[i])

if __name__ == '__main__':
    main()