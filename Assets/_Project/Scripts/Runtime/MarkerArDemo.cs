using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Webcam marker AR demo with an automatic no-camera simulation.</summary>
public sealed class MarkerArDemo : MonoBehaviour
{
    const int W = 640, H = 360;
    const float ViewHeight = 10f;
    Camera cam;
    Renderer feedRenderer;
    GameObject model;
    WebCamTexture webcam;
    Texture2D demoTexture;
    Texture2D sunMarker;
    Texture activeTexture;
    bool demoMode = true, markerVisible;
    string status = "正在初始化…";
    float nextDetection;
    Vector2 markerViewport = new Vector2(.5f, .5f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<MarkerArDemo>() == null)
            new GameObject("Marker AR Demo").AddComponent<MarkerArDemo>();
    }

    IEnumerator Start()
    {
        BuildScene();
        EnableDemo("正在檢查攝影機…");
        yield return StartCoroutine(StartInput());
    }

    void BuildScene()
    {
        foreach (Camera c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
        GameObject cameraObject = new GameObject("AR Camera");
        cam = cameraObject.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = ViewHeight * .5f;
        cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.035f, .045f, .07f);

        GameObject feed = GameObject.CreatePrimitive(PrimitiveType.Quad);
        feed.name = "Camera Feed";
        feed.transform.position = new Vector3(0, 0, 2);
        feedRenderer = feed.GetComponent<Renderer>();
        feedRenderer.material = new Material(Shader.Find("Unlit/Texture"));
        sunMarker = Resources.Load<Texture2D>("SunMarker");
        model = LoadApolloCar();
        model.SetActive(false);

        Light light = new GameObject("AR Key Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        light.transform.rotation = Quaternion.Euler(35, -30, 0);
    }

    static GameObject LoadApolloCar()
    {
        GameObject prefab = Resources.Load<GameObject>("ApolloIE/ApolloIE");
        GameObject root = new GameObject("Tracked Apollo Intensa Emozione");
        if (prefab == null)
        {
            Debug.LogError("ApolloIE model is missing from Resources/ApolloIE.");
            return root;
        }

        GameObject car = Instantiate(prefab, root.transform, false);
        car.name = "Apollo Intensa Emozione (Black Snow)";

        Renderer[] renderers = car.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float longestSide = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = longestSide > .001f ? 3.4f / longestSide : 1f;
            car.transform.localScale = Vector3.one * scale;
            car.transform.localPosition = -bounds.center * scale;
        }

        root.transform.rotation = Quaternion.identity;
        return root;
    }

    IEnumerator StartInput()
    {
        if (WebCamTexture.devices.Length == 0) { EnableDemo("找不到攝影機，已啟用無攝影機示範模式"); yield break; }
        webcam = new WebCamTexture(WebCamTexture.devices[0].name, W, H, 30);
        webcam.Play();
        float deadline = Time.realtimeSinceStartup + 4;
        while (webcam.width <= 16 && Time.realtimeSinceStartup < deadline) yield return null;
        if (webcam.width <= 16) { webcam.Stop(); EnableDemo("攝影機無法啟動，已切換成示範模式"); yield break; }
        demoMode = false;
        activeTexture = webcam;
        feedRenderer.material.mainTexture = activeTexture;
        status = "攝影機模式：將洋紅色符號放入畫面";
        FitFeed(webcam.width, webcam.height);
    }

    void EnableDemo(string message)
    {
        demoMode = true;
        activeTexture = sunMarker;
        feedRenderer.material.mainTexture = activeTexture;
        status = message;
        FitFeed(1, 1);
    }

    void FitFeed(int width, int height)
    {
        feedRenderer.transform.localScale = new Vector3(ViewHeight * Mathf.Max(.1f, (float)width / height), ViewHeight, 1);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            if (demoMode && webcam != null && webcam.width > 16) { demoMode = false; activeTexture = webcam; feedRenderer.material.mainTexture = webcam; FitFeed(webcam.width, webcam.height); }
            else EnableDemo("手動切換：無攝影機示範模式");
        }
        if (demoMode)
        {
            markerVisible = true;
            markerViewport = new Vector2(.5f, .5f);
        }
        else if (webcam != null && webcam.didUpdateThisFrame && Time.unscaledTime >= nextDetection)
        {
            nextDetection = Time.unscaledTime + .12f;
            DetectSunMarker(webcam.GetPixels32(), webcam.width, webcam.height);
        }
        UpdateModel();
    }

    void DetectSunMarker(Color32[] pixels, int width, int height)
    {
        long sx=0, sy=0; int count=0;
        for (int y=0; y<height; y+=4) for (int x=0; x<width; x+=4)
        {
            Color32 p = pixels[y*width+x];
            int luminance = (p.r * 3 + p.g * 6 + p.b) / 10;
            if (luminance < 70) { sx+=x; sy+=y; count++; }
        }
        markerVisible = count >= Mathf.Max(40, width*height/2500);
        if (markerVisible) { markerViewport = new Vector2(Mathf.Clamp01((float)sx/count/width), Mathf.Clamp01((float)sy/count/height)); status="已辨識太陽符號：顯示 Apollo 超跑"; }
        else status="搜尋符號中…請出示黑白太陽圖卡";
    }

    void UpdateModel()
    {
        model.SetActive(markerVisible);
        if (!markerVisible) return;
        float aspect = activeTexture == null ? 16f/9f : (float)activeTexture.width/activeTexture.height;
        Vector3 target = new Vector3((markerViewport.x-.5f)*ViewHeight*aspect, (markerViewport.y-.5f)*ViewHeight, 0);
        model.transform.position = Vector3.Lerp(model.transform.position, target, 10*Time.deltaTime);
        // Keep the car level and rotate it around its vertical axis like a showroom turntable.
        model.transform.rotation = Quaternion.Euler(0f, Time.time * 28f, 0f);
        model.transform.localScale = Vector3.one * (.95f + Mathf.Sin(Time.time*2.5f)*.025f);
    }

    void OnGUI()
    {
        GUIStyle title=new GUIStyle(GUI.skin.label){fontSize=Mathf.RoundToInt(Mathf.Clamp(Screen.height*.04f,22,38)),fontStyle=FontStyle.Bold}; title.normal.textColor=Color.white;
        GUIStyle body=new GUIStyle(GUI.skin.box){fontSize=Mathf.RoundToInt(Mathf.Clamp(Screen.height*.023f,15,24)),alignment=TextAnchor.MiddleLeft}; body.normal.textColor=Color.white;
        GUI.Label(new Rect(24,18,Screen.width-48,55),"太陽圖卡 AR－Apollo 超跑",title);
        GUI.Box(new Rect(24,Screen.height-100,Screen.width-48,70),status+"\n空白鍵：切換攝影機／示範模式",body);
        if(demoMode) GUI.Label(new Rect(Screen.width-240,24,210,45),"DEMO MODE",title);
    }

    void OnDestroy() { if(webcam!=null && webcam.isPlaying) webcam.Stop(); }
}
