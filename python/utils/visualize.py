import numpy as np
import cv2
from collections import deque
from typing import List, Tuple, Dict, Any, Union
try:
    import ujson as json
except:
    import json
import os
import time

# a sliding window for recognition results in the last ~1.7 seconds at 30fps
sliding_window = deque(maxlen=50)
sliding_window_size = 50
def add_to_sliding_window(result: Tuple) -> None:
    global sliding_window
    sliding_window.append((result, time.time()))


def get_result(timestamp: float) -> Union[Tuple, None]:
    # find the closest image in the sliding window to the timestamp
    global sliding_window
    closest_result = None
    closest_diff = float('inf')
    for result, ts in sliding_window:
        diff = abs(ts - timestamp)
        if diff < closest_diff:
            closest_diff = diff
            closest_result = result
    return closest_result

GLOBAL_DELAY = 0.00 # 10ms delay for the sliding window
OFFSET = (0, -3) # offset for contour and geometry center


TOP_LEFT = (0.15696466, 0.185185)
BOTTOM_RIGHT = (0.84303534, 0.9407407)

json_path = os.path.join(os.path.dirname(__file__), 'spriteDict.json')
with open(json_path, 'r') as f:
    spriteDict: Dict[str, str] = json.load(f)

STICK_LEN = 48
STICK_WIDTH = 4
STICK_OFFSET = 2
ICON_RATIO = 0.12

icon_path = os.path.join(os.path.dirname(__file__), '../../unity/Assets/Icons')
iconDict: Dict[str, np.ndarray] = {} # {name: icon}
for key, value in spriteDict.items():
    # icon = cv2.imread(os.path.join(icon_path, value + '.png'), cv2.IMREAD_GRAYSCALE)
    # open with numpy in case there's non-ASCII characters in the path
    icon = cv2.imdecode(np.fromfile(os.path.join(icon_path, value + '.png'), dtype=np.uint8), cv2.IMREAD_UNCHANGED)[..., 3]
    icon = cv2.threshold(icon, 128, 255, cv2.THRESH_BINARY)[1]
    icon = cv2.resize(icon, (0, 0), fx=ICON_RATIO, fy=ICON_RATIO)

    # if 'bicyclist' in key or 'motorcyclist' in key:
    if 'bicycle' in key or 'motorcyclist' in key:
        continue # skip these two icons as they are not used in the current model

    iconDict[key] = icon


last_result = None

def draw_result(image: np.ndarray, result: Dict[str, Any],
                priority_mapping: Dict[str, str] = {},
                mldemo: bool = False,
                colors: Union[Tuple[int, int, int], Dict[str, Tuple[int, int, int]]] = (0, 255, 255),
                label_colors: Union[Tuple[int, int, int], Dict[str, Tuple[int, int, int]]] = (0, 255, 255),
                label_size: float = 1.0,
                thickness: Union[int, Dict[str, int]] = 3,
                designs: Union[str, Dict[str, str]] = 'outline',
                name_mapping: Dict[str, str] = {},
                delay: float = GLOBAL_DELAY,
                override: bool = False,
                ) -> np.ndarray:
    global last_result
    now = time.time()

    result = (result, colors, label_colors, label_size, thickness, designs, name_mapping)
    add_to_sliding_window(result)

    if override:
        result_ = last_result
    else:
        result_ = get_result(now - delay)
        last_result = result_

    if result_ is None:
        return image
    result, colors, label_colors, label_size, thickness, designs, name_mapping = result_
    
    boxes = result['boxes']
    mask_contours = result['mask_contours']
    class_names = result['class_names']
    geometry_center = result['geometry_center']
    scores = result['scores']

    # draw the visible area
    # x1, y1 = (int(image.shape[1] * TOP_LEFT[0]), int(image.shape[0] * TOP_LEFT[1]))
    # x2, y2 = (int(image.shape[1] * BOTTOM_RIGHT[0]), int(image.shape[0] * BOTTOM_RIGHT[1]))
    # cv2.rectangle(image, (x1, y1), (x2, y2), (0, 0, 255), 1)

    if len(class_names) == 0:
        return image
    
    to_draw = list(zip(boxes, mask_contours, class_names, geometry_center, scores))
    to_draw = sorted(to_draw, key = lambda x: priority_mapping.get(x[2], "z"), reverse=True)

    # make image 4 channels
    # image = cv2.cvtColor(image, cv2.COLOR_BGR2BGRA)
    
    for box, contour, name, center, score in to_draw:
        mode = designs[name] if isinstance(designs, dict) else designs
        color = colors[name] if isinstance(colors, dict) else colors
        alpha = color[3]/100 if len(color) == 4 else 1
        color = (color[2], color[1], color[0])
        label_color = label_colors[name] if isinstance(label_colors, dict) else label_colors
        label_alpha = label_color[3]/100 if len(label_color) == 4 else 1
        label_color = (label_color[2], label_color[1], label_color[0])
        t = thickness[name] if isinstance(thickness, dict) else thickness

        display_name = name_mapping.get(name, name)

        x1, y1, x2, y2 = box
        contour = np.array(contour) + np.array([x1, y1])
        contour = np.array(contour, dtype=np.int32)

        if mldemo:
            # for ml demo, draw the box and mask only
            cv2.rectangle(image, (x1, y1), (x2, y2), color, 2)

            cv2.drawContours(image, [contour], -1, color, 2)

            # mask = image.copy()
            # cv2.fillPoly(mask, [contour], color)
            # image = cv2.addWeighted(image, 1 - alpha, mask, alpha, 0)

            # display the name on the top left corner with a black background
            fontsize = 0.5
            thickness = 1
            text = f"{display_name} ({score:.2f})"
            textSize = cv2.getTextSize(text, cv2.FONT_HERSHEY_SIMPLEX, fontsize, thickness)[0]

            # bottom left corner of the box
            textPos = (x1, max(y1 - textSize[1]//2, 0))

            # draw a background for the text
            cv2.rectangle(image, (textPos[0], textPos[1] - textSize[1]//2 - 2), (textPos[0] + textSize[0], textPos[1] + textSize[1]//2), (0, 0, 0), thickness=-1)
            cv2.putText(image, text, (textPos[0], textPos[1]+3), cv2.FONT_HERSHEY_SIMPLEX, fontsize, (255, 255, 255), thickness)

            continue
        else:
            # apply the offset to the geometry center
            center = (int(center[0] + OFFSET[0]), int(center[1] + OFFSET[1]))
            # apply the offset to the contour
            contour = contour + np.array(OFFSET, dtype=np.int32)
            if 'outline' in mode:
                mask = image.copy()
                cv2.drawContours(mask, [contour], -1, color, t)
                image = cv2.addWeighted(image, 1 - alpha, mask, alpha, 0)
            if 'solid' in mode:
                solid_alpha = min(0.6, alpha)
                mask = image.copy()
                cv2.fillPoly(mask, [contour], color)
                image = cv2.addWeighted(image, 1 - solid_alpha, mask, solid_alpha, 0)
            if 'icon' in mode:
                # if can't find the icon, skip
                if display_name not in iconDict:
                    # print(display_name, 'icon not found!')
                    continue

                swidth = round(STICK_WIDTH * label_size)
                slen = round(STICK_LEN * label_size)
                soffset = round(STICK_OFFSET * label_size)
                image_copy = image.copy()
                # first, draw the stick that goes from the geometry center up. Filled
                pt1 = (int(center[0] + (x2-x1)*0.1 - swidth // 2), center[1] - slen)
                pt2 = (int(center[0] + (x2-x1)*0.1 + swidth // 2), center[1])
                cv2.rectangle(image_copy, pt1, pt2, label_color, -1)

                # then, draw the icon on top of the stick with the offset
                icon = iconDict[display_name]
                # scale the icon based on label size
                icon = cv2.resize(icon, (0, 0), fx=label_size, fy=label_size, interpolation=cv2.INTER_CUBIC)
                # calculate the position of the icon
                icon_top_left = (pt1[0] - icon.shape[1] // 2, pt1[1] - icon.shape[0] - soffset)
                icon_bottom_right = (icon_top_left[0] + icon.shape[1], icon_top_left[1] + icon.shape[0])
                # if the icon is out of the image, draw part of it
                if icon_top_left[0] < 0:
                    icon = icon[:, -icon_top_left[0]:]
                    icon_top_left = (0, icon_top_left[1])
                if icon_top_left[1] < 0:
                    icon = icon[-icon_top_left[1]:, :]
                    icon_top_left = (icon_top_left[0], 0)
                if icon_bottom_right[0] > image.shape[1]:
                    icon = icon[:, :image.shape[1] - icon_top_left[0]]
                    icon_bottom_right = (image.shape[1], icon_bottom_right[1])
                if icon_bottom_right[1] > image.shape[0]:
                    icon = icon[:image.shape[0] - icon_top_left[1], :]
                    icon_bottom_right = (icon_bottom_right[0], image.shape[0])
                # now draw the icon
                mask = icon > 0
                image_copy[icon_top_left[1]:icon_bottom_right[1], icon_top_left[0]:icon_bottom_right[0]][mask] = label_color

                # blend the image with the icon
                image = cv2.addWeighted(image, 1 - label_alpha, image_copy, label_alpha, 0)

        # fontsize = 1
        # fontthickness = 2
        # font = cv2.FONT_HERSHEY_SIMPLEX

        # textsize = cv2.getTextSize(name, font, fontsize, fontthickness)[0]
        # textX = center[0] - textsize[0] // 2
        # textY = center[1] + textsize[1] // 2

        # cv2.putText(image, name, (textX, textY), cv2.FONT_HERSHEY_SIMPLEX, fontsize, (0, 0, 255), fontthickness)

    return image

if __name__ == "__main__":
    print(spriteDict)