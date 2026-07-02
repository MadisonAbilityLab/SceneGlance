using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Windows.WebCam;
using Microsoft.MixedReality.Toolkit;
// using Microsoft.MixedReality.Toolkit.SpatialAwareness;
using System.Runtime.InteropServices;
using AOT;
using System;
using UnityEngine.UI;

public class OpenCVCamera : MonoBehaviour
{
    private PhotoCapture photoCaptureObject = null;
    public uint cameraResolutionWidth = 640;
    public uint cameraResolutionHeight = 360;
    public uint cameraFramerate = 30;
    public int lineThickness = 1;
    public float screenDistance = 1.0f;
    public float yOffset = 0.0f;
    public float timeOut = 10.0f;
    float startedTime = 0.0f;
    private Matrix4x4 projectionMatrix = Matrix4x4.identity;
    private Matrix4x4 inverseProjectionMatrix = Matrix4x4.identity;

    // Start() creates a PhotoCapture object and wait for it to be initialized
    // However, it is possible that Init() is called before Start() is finished
    // In this case, we will save the Init() part in this variable
    // and call it when Start() is finished
    delegate void InitCallback(uint width, uint height, uint fps);
    InitCallback initCallback = null;
    // temporary variable to store the camera's position and rotation provided externally
    [Tooltip("Auxiliary game object to store external camera position and rotation")]
    public GameObject auxiliaryCamera = null;
    GameObject dummyMainCamera = null;
    GameObject dummyPlane = null;
    Canvas screenCanvas = null;
    RawImage screenImage = null;
    // pre-defined local position and rotation of the camera
    Vector3 initialPosition = new Vector3(0.00f, 0.03f, 0.06f);
    Quaternion initialRotation = Quaternion.Euler(4.58f, 359.93f, 0.00f);
    Vector3 externalPosOffset = new Vector3(0.01f, 0.00f, -0.01f);
    Vector3 externalRotOffset = new Vector3(-1.02f, 0.58f, -3.0f);
    float anchor2DDistance = 0f;
    public float anchorPlaneWidth { get; private set; } = 0f;
    public float anchorPlaneHeight { get; private set; } = 0f;
    Vector2[] quadCorners = new Vector2[4];
    Texture2D photoTexture = null;
    Texture2D processedTexture;
    bool firstRun = true;

    [Header("Canny Parameters")]
    [SerializeField] [Range(0, 400)] private float threshold1 = 150f;
    [SerializeField] [Range(0, 400)] private float threshold2 = 300f;

    [Header("Gaussian Blur Parameters")]
    [SerializeField] [Range(1, 31)] private int kernelSize = 5;
    [SerializeField] [Range(0.1f, 10f)] private float sigmaX = 1.5f;
    [SerializeField] [Range(0.1f, 10f)] private float sigmaY = 1.5f;
    private GCHandle srcHandle;
    private GCHandle dstHandle;
    private byte[] srcBytes;
    private byte[] dstBytes;
    private bool resourcesAllocated = false;

    [DllImport("OpenCVHoloLensWrapper")]
    private static extern void GaussianCannyProcess(
        IntPtr src, IntPtr dst, 
        float threshold1, float threshold2,
        int kernelSize, double sigmaX, double sigmaY,
        int width, int height, int lineThickness = 1);
        
    [DllImport("OpenCVHoloLensWrapper")]
    private static extern void CannyProcess(
        IntPtr src, IntPtr dst, 
        float threshold1, float threshold2,
        int width, int height, int lineThickness = 1);

    void Start()
    {
        // we want to make this object a child of the camera
        // first create a PhotoCapture object, but don't initialize it yet
        // when called Init(width, height, fps), it will be initialized with the camera's current settings
        // then we can take down and update this camera's position and rotation
        PhotoCapture.CreateAsync(false, delegate (PhotoCapture captureObject) {
            photoCaptureObject = captureObject;

            // if Init() was called before Start() is finished, call it now
            if (initCallback != null)
            {
                initCallback(cameraResolutionWidth, cameraResolutionHeight, cameraFramerate);
                initCallback = null;
            }
        });

        // set the position and rotation of this object
        transform.localPosition = initialPosition;
        transform.localRotation = initialRotation;

        // set the projection matrix to a pre-defined value:
        // 1.51550 0.00000 0.01660 0.00000
        // 0.00000 2.69317 -0.03920        0.00000
        // 0.00000 0.00000 -1.00401        -0.20040
        // 0.00000 0.00000 -1.00000        0.00000
        // projectionMatrix = new Matrix4x4(
        //     new Vector4(1.51550f, 0.00000f, 0.01660f, 0.00000f),
        //     new Vector4(0.00000f, 2.69317f, -0.03920f, 0.00000f),
        //     new Vector4(0.00000f, 0.00000f, -1.00401f, -0.20040f),
        //     new Vector4(0.00000f, 0.00000f, -1.00000f, 0.00000f)
        // );
        // inverseProjectionMatrix = projectionMatrix.inverse;

        // Call Init()
        Init(cameraResolutionWidth, cameraResolutionHeight, cameraFramerate);

        if (auxiliaryCamera == null)
        {
            DebugServer.SendDebugMessage("AuxiliaryCamera not found");
        }
        else
        {
            // dummyMainCamera is a child of the auxiliaryCamera named "DummyMainCamera"
            dummyMainCamera = auxiliaryCamera.transform.Find("DummyMainCamera").gameObject;
            if (dummyMainCamera == null)
            {
                DebugServer.SendDebugMessage("DummyMainCamera not found");
            }

            // dummyPlane is a child of the auxiliaryCamera named "DummyPlane"
            dummyPlane = auxiliaryCamera.transform.Find("DummyPlane").gameObject;
            if (dummyPlane == null)
            {
                DebugServer.SendDebugMessage("DummyPlane not found");
            }
        }

        // find a "Canvas" component as a child of the locatable camera
        screenCanvas = GetComponentInChildren<Canvas>();
        if (screenCanvas == null)
        {
            DebugServer.SendDebugMessage("Canvas not found");
        }
        else
        {
            // find a "RawImage" component as a child of the canvas
            screenImage = screenCanvas.GetComponentInChildren<RawImage>();
            if (screenImage == null)
            {
                DebugServer.SendDebugMessage("RawImage not found");
            }
        }
    }

    public void Init(uint width, uint height, uint fps)
    {
        // set the camera's resolution and framerate
        // this is the resolution and framerate of the video stream
        // the resolution of the photo is set in TakePhoto()
        // the resolution of the video stream is set in the PhotoCapture.CreateAsync() call in Start()
        cameraResolutionWidth = width;
        cameraResolutionHeight = height;
        cameraFramerate = fps;

        // set the photo texture
        photoTexture = new Texture2D((int)cameraResolutionWidth, (int)cameraResolutionHeight, TextureFormat.BGRA32, false);

        // if Init() is called before Start() is finished, save the parameters in this variable
        // and call it when Start() is finished
        if (photoCaptureObject == null)
        {
            initCallback = Init;
            return;
        }

        CameraParameters c = new CameraParameters();
        c.hologramOpacity = 0.0f;
        c.cameraResolutionWidth = (int)cameraResolutionWidth;
        c.cameraResolutionHeight = (int)cameraResolutionHeight;
        c.frameRate = (int)cameraFramerate;
        c.pixelFormat = CapturePixelFormat.BGRA32;

        photoCaptureObject.StartPhotoModeAsync(c, delegate (PhotoCapture.PhotoCaptureResult result) {
            // Take a picture
            photoCaptureObject.TakePhotoAsync(OnCapturedPhotoToMemory);
        });
    }


    void OnCapturedPhotoToMemory(PhotoCapture.PhotoCaptureResult result, PhotoCaptureFrame photoCaptureFrame)
    {
        if (!result.success)
        {
            DebugServer.SendDebugMessage("Failed to capture photo to memory.");
            return;
        }
        Matrix4x4 cameraToWorldMatrix;
        photoCaptureFrame.TryGetCameraToWorldMatrix(out cameraToWorldMatrix);
        photoCaptureFrame.TryGetProjectionMatrix(Camera.main.nearClipPlane, Camera.main.farClipPlane, out projectionMatrix);
        inverseProjectionMatrix = projectionMatrix.inverse;

        // if in Unity Editor, then reset the position and rotation
        Vector3 cameraPosition = cameraToWorldMatrix.GetColumn(3);
        Quaternion cameraRotation = Quaternion.LookRotation(-cameraToWorldMatrix.GetColumn(2), cameraToWorldMatrix.GetColumn(1));
        if (Application.isEditor)
        {
            // set the position of this object
            transform.position = cameraPosition;
            // set the rotation of this object
            transform.rotation = cameraRotation;
        }
        // stop the photo mode
        // photoCaptureObject.StopPhotoModeAsync(OnStoppedPhotoMode);

        // // DEBUG: take another photo
        // photoCaptureObject.TakePhotoAsync(OnCapturedPhotoToMemory);

        if (firstRun) {
            quadCorners = new Vector2[4] {
                new Vector2(0, 0),
                new Vector2(cameraResolutionWidth, 0),
                new Vector2(cameraResolutionWidth, cameraResolutionHeight),
                new Vector2(0, cameraResolutionHeight)
            };

            Vector3[] worldCorners = new Vector3[4];
            Vector3[] worldSpaceDirs = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                // Vector3 worldSpaceDir = PixelCoordToWorldCoord(quadCorners[i]).Item1;
                worldSpaceDirs[i] = PixelCoordToWorldCoord(quadCorners[i]).Item1;
                Vector3 worldSpacePoint = transform.position + worldSpaceDirs[i] * screenDistance;
                worldCorners[i] = worldSpacePoint;
            }

            Vector3 videoRendererPosition = Vector3.zero;
            for (int i = 0; i < 4; i++)
            {
                videoRendererPosition += worldCorners[i];
            }
            videoRendererPosition /= 4.0f;

            // compute the distance between the opencv camera (self) and the video renderer
            float distance = Vector3.Distance(videoRendererPosition, transform.position);
            // move it so that the center of the plane is at the given distance
            videoRendererPosition += transform.forward * (distance - screenDistance);
            // set the world corners of the plane
            for (int i = 0; i < 4; i++)
            {
                worldCorners[i] += worldSpaceDirs[i] * (distance - screenDistance);
            }

            auxiliaryCamera.transform.position = transform.position;
            auxiliaryCamera.transform.rotation = transform.rotation;
            // dummy main camera reflects the position and rotation of the main camera
            dummyMainCamera.transform.position = Camera.main.transform.position;
            dummyMainCamera.transform.rotation = Camera.main.transform.rotation;

            // in the case of 2D rendering, there is a static offset between the camera and the object
            anchor2DDistance = Vector3.Distance(videoRendererPosition, transform.position);
            anchorPlaneWidth = Vector3.Distance(worldCorners[1], worldCorners[0]);
            anchorPlaneHeight = Vector3.Distance(worldCorners[3], worldCorners[0]);

            // set the position and rotation of the dummy plane
            dummyPlane.transform.position = videoRendererPosition;
            // plane should face the dummy main camera
            Vector3 planeDirection = (dummyPlane.transform.position - dummyMainCamera.transform.position).normalized;
            dummyPlane.transform.rotation = Quaternion.LookRotation(planeDirection, Vector3.up);

            // the screen is the same as the dummy plane
            screenCanvas.transform.position = dummyPlane.transform.position;
            screenCanvas.transform.forward = transform.forward;
            var zRotation = transform.rotation.eulerAngles.z;
            screenCanvas.transform.RotateAround(screenCanvas.transform.position, screenCanvas.transform.forward, zRotation);

            // set the ratio of the canvas
            screenCanvas.GetComponent<RectTransform>().sizeDelta = new Vector2(anchorPlaneWidth, anchorPlaneHeight);

            // scale the canvas based on the screen distance
            // screenCanvas.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f) * screenDistance;

            // offset of the screen canvas
            screenCanvas.transform.position += new Vector3(0, yOffset, 0);

            firstRun = false;

            startedTime = Time.time;
        }

        // set the texture of the photo to the photo texture
        photoCaptureFrame.UploadImageDataToTexture(photoTexture);

        try {
            ProcessImage(photoTexture);
        }
        catch (Exception e) {
            DebugServer.SendDebugMessage("Error processing image: " + e.Message);
        }

        if (Time.time - startedTime > timeOut) {
            // stop the photo mode
            photoCaptureObject.StopPhotoModeAsync(OnStoppedPhotoMode);
        }
        else {
            // capture another photo
            photoCaptureObject.TakePhotoAsync(OnCapturedPhotoToMemory);
        }
    }


    void OnStoppedPhotoMode(PhotoCapture.PhotoCaptureResult result)
    {
        // Shutdown the photo capture resource
        photoCaptureObject.Dispose();
    }

    void OnApplicationQuit()
    {
        if (photoCaptureObject != null)
        {
            photoCaptureObject.StopPhotoModeAsync(OnStoppedPhotoMode);
        }
    }

    public void SetParams(uint width, uint height, uint fps)
    {
        cameraResolutionWidth = width;
        cameraResolutionHeight = height;
        cameraFramerate = fps;
    }

    // internal function: convert a pixel coordinate to a world coordinate
    // providing a Matrix4x4 cameraToWorldMatrix, so that if an external
    // camera position and rotation is provided, we will compute a separate
    // projection matrix based on that, while if not provided, we will use
    // the projection matrix from the camera
    Vector3 _PixelCoordToWorldCoord(Vector2 pixelCoord, Matrix4x4 cameraToWorldMatrix)
    {
        // convert the pixel coordinate to normalized device coordinate (NDC)
        Vector2 ndcCoord = ConvertPixelToNDC(pixelCoord);

        float focalLengthX = projectionMatrix.GetColumn(0).x;
        float focalLengthY = projectionMatrix.GetColumn(1).y;
        float centerX = projectionMatrix.GetColumn(2).x;
        float centerY = projectionMatrix.GetColumn(2).y;

        // normalize factor
        float normFactor = projectionMatrix.GetColumn(2).z;
        centerX /= normFactor;
        centerY /= normFactor;

        // convert the NDC to camera space
        Vector3 dirRay = new Vector3((ndcCoord.x - centerX) / focalLengthX, (ndcCoord.y - centerY) / focalLengthY, -1.0f / normFactor);
        Vector3 direction = new Vector3(
            Vector3.Dot(cameraToWorldMatrix.GetRow(0), dirRay),
            Vector3.Dot(cameraToWorldMatrix.GetRow(1), dirRay),
            Vector3.Dot(cameraToWorldMatrix.GetRow(2), dirRay)
        );

        return direction.normalized;
    }

    // convert a pixel coordinate to a world coordinate
    // the pixel coordinate is relative to the top-left corner of the camera image
    // return: the world space direction of the ray from the camera to the pixel
    // the caller can use this direction to cast a ray from the camera to the pixel
    // camera's position is transform.position of this object
    public (Vector3, Vector3) PixelCoordToWorldCoord(Vector2 pixelCoord)
    {
        Matrix4x4 cameraToWorldMatrix = transform.localToWorldMatrix;
        return (_PixelCoordToWorldCoord(pixelCoord, cameraToWorldMatrix), Camera.main.transform.position);
    }

    void UpdateAuxCamera(Vector3 cameraPosition, Quaternion cameraRotation)
    {
        auxiliaryCamera.transform.position = cameraPosition + externalPosOffset;
        auxiliaryCamera.transform.rotation = cameraRotation;
        var finalRot = auxiliaryCamera.transform.rotation.eulerAngles + externalRotOffset;
        // set the z-axis to be the same as the internal camera
        auxiliaryCamera.transform.rotation = Quaternion.Euler(finalRot.x, finalRot.y, transform.rotation.eulerAngles.z);
    }

    // convert a pixel coordinate to a world coordinate
    // providing a camera position and rotation
    // return: the world space direction of the ray from the camera to the pixel
    public (Vector3, Vector3) PixelCoordToWorldCoord(Vector2 pixelCoord, Vector3 cameraPosition, Vector3 cameraRotation)
    {
        return PixelCoordToWorldCoord(pixelCoord, cameraPosition, Quaternion.LookRotation(cameraRotation));
    }

    public (Vector3, Vector3) PixelCoordToWorldCoord(Vector2 pixelCoord, Vector3 cameraPosition, Quaternion cameraRotation)
    {
        if (auxiliaryCamera == null)
        {
            // Alas! The external camera is not found, downgrading to the internal camera
            DebugServer.SendDebugMessage("AuxiliaryCamera not found, downgrading to the internal camera");
            return PixelCoordToWorldCoord(pixelCoord);
        }
        
        UpdateAuxCamera(cameraPosition, cameraRotation);

        var cameraToWorldMatrix = auxiliaryCamera.transform.localToWorldMatrix;
        return (_PixelCoordToWorldCoord(pixelCoord, cameraToWorldMatrix), auxiliaryCamera.transform.position);
    }

    Vector2 ConvertPixelToNDC(Vector2 pixelCoord)
    {
        float halfWidth = cameraResolutionWidth / 2.0f;
        float halfHeight = cameraResolutionHeight / 2.0f;

        pixelCoord.x = (pixelCoord.x - halfWidth) / halfWidth;
        pixelCoord.y = (pixelCoord.y - halfHeight) / halfHeight;

        pixelCoord.y = -pixelCoord.y;

        return pixelCoord;
    }

    public void ProcessImage(Texture2D sourceTexture)
    {
        if (sourceTexture == null) return;
        
        photoTexture = sourceTexture;
        int bufferSize = photoTexture.width * photoTexture.height * 4; // RGBA = 4 bytes per pixel
        
        // Initialize or resize the processed texture if needed
        if (processedTexture == null || 
            processedTexture.width != photoTexture.width || 
            processedTexture.height != photoTexture.height)
        {
            if (processedTexture != null)
                Destroy(processedTexture);
                
            processedTexture = new Texture2D(
                photoTexture.width, 
                photoTexture.height, 
                TextureFormat.RGBA32, false);
                
            // Free old resources if they exist
            FreeResources();
            
            // Allocate new buffers of the right size
            srcBytes = new byte[bufferSize];
            dstBytes = new byte[bufferSize];
            
            // Pin memory
            srcHandle = GCHandle.Alloc(srcBytes, GCHandleType.Pinned);
            dstHandle = GCHandle.Alloc(dstBytes, GCHandleType.Pinned);
            resourcesAllocated = true;
        }
        
        // Copy the source texture data to our reusable buffer
        if (photoTexture.GetRawTextureData().Length == bufferSize)
        {
            Buffer.BlockCopy(photoTexture.GetRawTextureData(), 0, srcBytes, 0, bufferSize);
        }
        else
        {
            Debug.LogError("Texture data size mismatch!");
            return;
        }
        
        try
        {
            // Call the OpenCV wrapper
            GaussianCannyProcess(
                srcHandle.AddrOfPinnedObject(),
                dstHandle.AddrOfPinnedObject(),
                threshold1, 
                threshold2,
                kernelSize,
                sigmaX,
                sigmaY,
                photoTexture.width,
                photoTexture.height,
                lineThickness
            );
            // CannyProcess(
            //     srcHandle.AddrOfPinnedObject(),
            //     dstHandle.AddrOfPinnedObject(),
            //     threshold1, 
            //     threshold2,
            //     photoTexture.width,
            //     photoTexture.height,
            //     lineThickness
            // );
            
            // Update processed texture
            processedTexture.LoadRawTextureData(dstBytes);
            processedTexture.Apply();
            
            // Display the processed image
            screenImage.texture = processedTexture;
        }
        catch (Exception e)
        {
            Debug.LogError("Error processing image: " + e.Message);
        }
    }
    
    private void FreeResources()
    {
        if (resourcesAllocated)
        {
            if (srcHandle.IsAllocated) srcHandle.Free();
            if (dstHandle.IsAllocated) dstHandle.Free();
            resourcesAllocated = false;
        }
    }
    
    void OnDestroy()
    {
        FreeResources();
        
        if (processedTexture != null)
        {
            Destroy(processedTexture);
            processedTexture = null;
        }
    }
}
