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
        model = BuildTurtle();
        model.SetActive(false);

        Light light = new GameObject("AR Key Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        light.transform.rotation = Quaternion.Euler(35, -30, 0);
    }

    static GameObject BuildTurtle()
    {
        GameObject root = new GameObject("Tracked Turtle Model");
        AddPart(root.transform, "Shell", Vector3.zero, new Vector3(1.7f, .45f, 1.25f), new Color(.1f, .62f, .34f));
        AddPart(root.transform, "Head", new Vector3(0, 0, -1.05f), new Vector3(.58f, .42f, .58f), new Color(.4f, .88f, .52f));
        Vector3[] fins = { new Vector3(-1.05f,0,-.55f), new Vector3(1.05f,0,-.55f), new Vector3(-.95f,0,.65f), new Vector3(.95f,0,.65f) };
        foreach (Vector3 p in fins) AddPart(root.transform, "Flipper", p, new Vector3(.7f,.14f,.32f), new Color(.28f,.78f,.43f));
        root.transform.rotation = Quaternion.Euler(55, 0, 0);
        return root;
    }

    static void AddPart(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        Renderer r = part.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        r.material = new Material(shader);
        r.material.color = color;
        Collider collider = part.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
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
        if (demoTexture == null) { demoTexture = new Texture2D(W, H, TextureFormat.RGBA32, false); demoTexture.wrapMode = TextureWrapMode.Clamp; }
        activeTexture = demoTexture;
        feedRenderer.material.mainTexture = activeTexture;
        status = message;
        FitFeed(W, H);
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
            if (demoTexture == null) EnableDemo("無攝影機示範模式");
            DrawDemo(); markerVisible = true;
            markerViewport = new Vector2(.5f + Mathf.Sin(Time.time*.65f)*.24f, .5f + Mathf.Cos(Time.time*.8f)*.15f);
        }
        else if (webcam != null && webcam.didUpdateThisFrame && Time.unscaledTime >= nextDetection)
        {
            nextDetection = Time.unscaledTime + .12f;
            Detect(webcam.GetPixels32(), webcam.width, webcam.height);
        }
        UpdateModel();
    }

    void Detect(Color32[] pixels, int width, int height)
    {
        long sx=0, sy=0; int count=0;
        for (int y=0; y<height; y+=4) for (int x=0; x<width; x+=4)
        {
            Color32 p = pixels[y*width+x];
            if (p.r>155 && p.b>135 && p.g<125 && p.r>p.g*1.35f && p.b>p.g*1.2f) { sx+=x; sy+=y; count++; }
        }
        markerVisible = count >= Mathf.Max(12, width*height/9000);
        if (markerVisible) { markerViewport = new Vector2(Mathf.Clamp01((float)sx/count/width), Mathf.Clamp01((float)sy/count/height)); status="已辨識符號：顯示海龜模型"; }
        else status="搜尋符號中…請出示洋紅色方形標記";
    }

    void UpdateModel()
    {
        model.SetActive(markerVisible);
        if (!markerVisible) return;
        float aspect = activeTexture == null ? 16f/9f : (float)activeTexture.width/activeTexture.height;
        Vector3 target = new Vector3((markerViewport.x-.5f)*ViewHeight*aspect, (markerViewport.y-.5f)*ViewHeight, 0);
        model.transform.position = Vector3.Lerp(model.transform.position, target, 10*Time.deltaTime);
        model.transform.Rotate(Vector3.up, 35*Time.deltaTime, Space.World);
        model.transform.localScale = Vector3.one * (.68f + Mathf.Sin(Time.time*2.5f)*.04f);
    }

    void DrawDemo()
    {
        int mx=Mathf.RoundToInt((.5f+Mathf.Sin(Time.time*.65f)*.24f)*W), my=Mathf.RoundToInt((.5f+Mathf.Cos(Time.time*.8f)*.15f)*H);
        Color32 a=new Color32(18,31,48,255), b=new Color32(30,53,70,255), magenta=new Color32(245,30,210,255), white=new Color32(255,255,255,255);
        Color32[] pixels=demoTexture.GetPixels32();
        for(int y=0;y<H;y++) for(int x=0;x<W;x++) pixels[y*W+x]=((x/40+y/40)%2==0)?a:b;
        for(int y=my-45;y<=my+45;y++) { if(y<0||y>=H) continue; for(int x=mx-45;x<=mx+45;x++) { if(x<0||x>=W) continue; pixels[y*W+x]=(Mathf.Abs(x-mx)<8||Mathf.Abs(y-my)<8)?white:magenta; } }
        demoTexture.SetPixels32(pixels); demoTexture.Apply(false);
    }

    void OnGUI()
    {
        GUIStyle title=new GUIStyle(GUI.skin.label){fontSize=Mathf.RoundToInt(Mathf.Clamp(Screen.height*.04f,22,38)),fontStyle=FontStyle.Bold}; title.normal.textColor=Color.white;
        GUIStyle body=new GUIStyle(GUI.skin.box){fontSize=Mathf.RoundToInt(Mathf.Clamp(Screen.height*.023f,15,24)),alignment=TextAnchor.MiddleLeft}; body.normal.textColor=Color.white;
        GUI.Label(new Rect(24,18,Screen.width-48,55),"圖像辨識 AR－海龜模型",title);
        GUI.Box(new Rect(24,Screen.height-100,Screen.width-48,70),status+"\n空白鍵：切換攝影機／示範模式",body);
        if(demoMode) GUI.Label(new Rect(Screen.width-240,24,210,45),"DEMO MODE",title);
    }

    void OnDestroy() { if(webcam!=null && webcam.isPlaying) webcam.Stop(); }
}
