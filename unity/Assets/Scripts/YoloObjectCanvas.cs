using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using MyBox;
using System;

public enum RenderMode
{
    Solid,
    Outline,
    FlashSolid,
    FlashOutline,
};

public class YoloObjectCanvas : RecognitionVisualization
{
    RectTransform rectTransform;
    RawImage rawImage;
    public RenderMode renderMode = RenderMode.Solid;
    // check if it's Outline or FlashOutline
    // if the render mode is contour, the width of the contour
    [ConditionalField("renderMode", false, RenderMode.Outline, RenderMode.FlashOutline)] public int contourWidth = 3;
    Camera mainCamera = null;
    LocatableCamera locatableCamera = null;
    Texture2D texture = null;
    const int MARGIN = 5; // margin for the contour in case it goes out of the bounding box
    List<Edge> _edges = new List<Edge>();
    List<Edge> _activeEdges = new List<Edge>();

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        rawImage = GetComponentInChildren<RawImage>();
        mainCamera = Camera.main;
        // locatable camera is a child of main camera
        locatableCamera = mainCamera.GetComponentInChildren<LocatableCamera>();
    }

    public override void ProcessRecognitionResult(
        Vector3 position,
        float distance,
        string class_name,
        Vector2 center,
        uint width,
        uint height,
        Color color,
        RecognitionData data,
        int index
    )
    {
        // set position
        transform.position = position;

        // be perpendicular to the locatable camera
        var forward = locatableCamera? locatableCamera.transform.forward : mainCamera.transform.forward;
        transform.forward = forward;
        // transform.Rotate(0, 180, 0);

        // overlay the z rotation of the camera
        var zRotation = locatableCamera? locatableCamera.transform.rotation.eulerAngles.z : mainCamera.transform.rotation.eulerAngles.z;
        transform.RotateAround(transform.position, transform.forward, zRotation);

        // parse the other data needed
        var box = data.Boxes[index];

        // bounding box
        var bounding_box = new int[4];
        bounding_box[0] = box.X1;
        bounding_box[1] = box.Y1;
        bounding_box[2] = box.X2;
        bounding_box[3] = box.Y2;

        // render the recognition result
        RenderRecognitionResult(position, distance, class_name, bounding_box, center, width, height, color, data, index);
    }

    void RenderRecognitionResult(
        Vector3 position,
        float distance,
        string class_name,
        int[] bounding_box,
        Vector2 center,
        uint width,
        uint height,
        Color color,
        RecognitionData data,
        int index
    )
    {
        // create a texture for the bounding box
        int boxWidth = bounding_box[2] - bounding_box[0];
        int boxHeight = bounding_box[3] - bounding_box[1];
        int canvasWidth = boxWidth + MARGIN * 2;
        int canvasHeight = boxHeight + MARGIN * 2;

        // Reuse texture: Reinitialize if size changed, create only if null
        if (texture == null)
        {
            texture = new Texture2D(canvasWidth, canvasHeight, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
        }
        else if (texture.width != canvasWidth || texture.height != canvasHeight)
        {
            texture.Reinitialize(canvasWidth, canvasHeight);
        }

        // Zero-copy pixel access via NativeArray
        var pixels = texture.GetRawTextureData<byte>();
        unsafe { UnsafeUtility.MemClear(pixels.GetUnsafePtr(), pixels.Length); }

        var contour = data.MaskContours[index];
        var innerContours = contour.InnerContours;
        Vector2[] mask_contour = new Vector2[innerContours.Count];
        // the contour points are relative to the bounding box, which is nice
        // but we need to flip the y-axis
        for (int i = 0; i < innerContours.Count; i++)
        {
            mask_contour[i] = new Vector2(innerContours[i].X + MARGIN, canvasHeight - (innerContours[i].Y + MARGIN));
        }

        // the canvas is located at `center`, but the mask is located at the center of the bounding box
        // so need to adjust the anchor
        // set the pivot point of the canvas so that it rotates around the given center
        var pivot = new Vector2(
            (float)(center.x - bounding_box[0] + MARGIN) / canvasWidth,
            1 - (float)(center.y - bounding_box[1] + MARGIN) / canvasHeight
        );
        rawImage.rectTransform.pivot = pivot;
        rawImage.rectTransform.anchoredPosition = Vector2.zero;

        // set the scale ratio of the mask
        float scaleX = (float)boxWidth / height;
        float scaleY = (float)boxHeight / height;
        rawImage.transform.localScale = new Vector3(scaleX, scaleY, 1);

        // scale by distance * magic number
        float magicNumber = 0.75f;
        rawImage.transform.localScale *= distance * magicNumber;

        if (renderMode == RenderMode.Solid || renderMode == RenderMode.FlashSolid)
        {
            // fill the mask
            ScanLineFill(pixels, BuildEdgeList(mask_contour), color, canvasWidth, canvasHeight);
        }
        else if (renderMode == RenderMode.Outline || renderMode == RenderMode.FlashOutline)
        {
            var cwidth = contourWidth;
            var thicknesses = data.ContourThicknesses;
            if (thicknesses.Count > index) {
                var remoteWidth = thicknesses[index];
                if (remoteWidth > 0) {
                    cwidth = remoteWidth;
                }
            }
            // draw the contour
            DrawContour(pixels, mask_contour, center, cwidth, color, canvasWidth, canvasHeight);
        }
        else
        {
            throw new System.ArgumentException("Invalid render mode: " + renderMode);
        }

        // Direct write — no LoadRawTextureData copy needed
        texture.Apply();
        rawImage.texture = texture;
    }

    List<Edge> BuildEdgeList(Vector2[] points)
    {
        _edges.Clear();

        for (int i = 0; i < points.Length; i++)
        {
            Vector2 start = points[i];
            Vector2 end = points[(i + 1) % points.Length];

            // ignore horizontal edges
            if (start.y == end.y)
            {
                continue;
            }
            _edges.Add(new Edge(start, end));
        }

        return _edges;
    }

    void ScanLineFill(NativeArray<byte> pixels, List<Edge> edges, Color color, int canvasWidth, int canvasHeight)
    {
        // color transparency maximum is 0.8f
        var fillColor = new Color(color.r, color.g, color.b, Mathf.Min(color.a, 0.8f));

        edges.Sort((a, b) => a.yMin.CompareTo(b.yMin));

        // prevent yStart from exceeding the height of the texture
        int yStart = Mathf.FloorToInt(edges[0].yMin);
        yStart = Mathf.Max(yStart, 0);

        // prevent yEnd from exceeding the height of the texture
        int yEnd = Mathf.CeilToInt(edges[edges.Count - 1].yMax);
        yEnd = Mathf.Min(yEnd, canvasHeight-1);

        _activeEdges.Clear();

        for (int y = yStart; y < yEnd; y++)
        {
            // remove edges that end at this y (reverse loop to avoid index shifting)
            for (int i = _activeEdges.Count - 1; i >= 0; i--)
            {
                if (Mathf.CeilToInt(_activeEdges[i].yMax) == y)
                    _activeEdges.RemoveAt(i);
            }

            // add edges that start at this y
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].yMin == y)
                    _activeEdges.Add(edges[i]);
            }

            // sort edges by x
            _activeEdges.Sort((a, b) => a.x.CompareTo(b.x));

            for (int i = 0; i < _activeEdges.Count; i += 2)
            {
                // fill the pixels between the pairs of edges

                // prevent xStart from exceeding the width of the texture
                int xStart = Mathf.CeilToInt(_activeEdges[i].x);
                xStart = Mathf.Max(xStart, 0);

                // prevent xEnd from exceeding the width of the texture
                int xEnd = Mathf.FloorToInt(_activeEdges[i + 1].x);
                xEnd = Mathf.Min(xEnd, canvasWidth-1);

                for (int x = xStart; x < xEnd; x++)
                {
                    SetPixelColor(pixels, x, y, fillColor, canvasWidth, canvasHeight);
                }
            }

            // update x for active edges (struct copy-back pattern)
            for (int i = 0; i < _activeEdges.Count; i++)
            {
                var edge = _activeEdges[i];
                edge.x += edge.mInverse;
                _activeEdges[i] = edge;
            }
        }
    }

    void DrawBorder(Texture2D texture, int width, int height, Color color)
    {
        for (int i = 0; i < width; i++)
        {
            texture.SetPixel(i, 0, color);
            texture.SetPixel(i, height - 1, color);
        }

        for (int i = 0; i < height; i++)
        {
            texture.SetPixel(0, i, color);
            texture.SetPixel(width - 1, i, color);
        }
    }

    void DrawLine(NativeArray<byte> pixels, Vector2 start, Vector2 end, Vector2 center, int width, Color color, int canvasWidth, int canvasHeight)
    {
        int x0 = (int)start.x;
        int y0 = (int)start.y;
        int x1 = (int)end.x;
        int y1 = (int)end.y;

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);

        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;

        int err = dx - dy;
        int err2;

        while (true)
        {
            DrawThickPoint(pixels, x0, y0, center, width, color, canvasWidth, canvasHeight);

            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            err2 = 2 * err;

            if (err2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (err2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    void DrawThickPoint(NativeArray<byte> pixels, int x, int y, Vector2 center, int width, Color color, int canvasWidth, int canvasHeight)
    {
        int halfWidth = width / 2;
        int neg_x = halfWidth, pos_x = halfWidth, neg_y = halfWidth, pos_y = halfWidth;

        if (width % 2 == 0)
        {
            if (x >= center.x)
                neg_x = halfWidth - 1;
            else
                pos_x = halfWidth - 1;

            if (y >= center.y)
                neg_y = halfWidth - 1;
            else
                pos_y = halfWidth - 1;
        }

        for (int i = x-neg_x; i <= x+pos_x; i++)
        {
            for (int j = y-neg_y; j <= y+pos_y; j++)
            {
                if (i >= 0 && i < canvasWidth && j >= 0 && j < canvasHeight)
                {
                    SetPixelColor(pixels, i, j, color, canvasWidth, canvasHeight);
                }
            }
        }
    }

    void DrawContour(NativeArray<byte> pixels, Vector2[] points, Vector2 center, int width, Color color, int canvasWidth, int canvasHeight)
    {
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 start = points[i];
            Vector2 end = points[(i + 1) % points.Length];
            DrawLine(pixels, start, end, center, width, color, canvasWidth, canvasHeight);
        }
    }

    void SetPixelColor(NativeArray<byte> pixels, int x, int y, Color color, int width, int height)
    {
        if (x >= 0 && x < width && y >= 0 && y < height) {
            // RGBA (4 bytes per pixel
            int index = (y * width + x) * 4;
            pixels[index] = (byte)(color.r * 255);
            pixels[index + 1] = (byte)(color.g * 255);
            pixels[index + 2] = (byte)(color.b * 255);
            pixels[index + 3] = (byte)(color.a * 255);
        }
        else {
            throw new System.ArgumentException("Invalid pixel coordinate: (" + x + ", " + y + ")" + " for texture size: " + width + "x" + height);
        }
    }

    void Update()
    {
        // if it's flashing type
        if (renderMode == RenderMode.FlashSolid || renderMode == RenderMode.FlashOutline)
        {
            // flash the object
            float alpha = Mathf.Abs(Mathf.Sin(Time.time * 2));
            rawImage.color = new Color(1, 1, 1, alpha);
        }
    }

    void OnDestroy()
    {
        if (texture != null)
        {
            Destroy(texture);
            texture = null;
        }
    }
}

struct Edge
{
    public float yMin, yMax, x, mInverse;

    public Edge(Vector2 start, Vector2 end)
    {
        if (start.y < end.y)
        {
            yMin = start.y;
            yMax = end.y;
            x = start.x;
        }
        else
        {
            yMin = end.y;
            yMax = start.y;
            x = end.x;
        }

        mInverse = (end.x - start.x) / (end.y - start.y);
    }
}
