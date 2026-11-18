using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
namespace SlimeLab
{
    public sealed partial class SlimeLabController:MonoBehaviour
    {
        public SlimeSimulation Simulation {get;private set;}
        SlimeSurface surface;SlimeSpray spray;SlimeMotor motor;SlimeFollowCamera rig;Camera view,environmentCamera;GameObject architecture;RenderTexture environment;
        SlimeColony colony;bool environmentReady;RenderTexture pendingEnvironment;int environmentFace=-1;
        readonly Dictionary<SlimeSimulation,SlimeSurface> followerSurfaces=new Dictionary<SlimeSimulation,SlimeSurface>();
        readonly List<SlimeSimulation> retiredFollowers=new List<SlimeSimulation>();
        Scenario scenario;float accumulator,uiScale=1;SlimeInput command;bool panel;
        Plane grabPlane;bool auto,showMetrics=false,highDetail;Vector3 pressScreen,tapPoint;float pressTime;
        Material stone,lightMaterial,labelMaterial;Font font;GUIStyle title,text,small,button,active,card;
        Task<CheckReport> checking;CheckReport lastReport;string status="TPS：移动鼠标环视，右键瞄准，Esc 释放鼠标。";
        bool captureMode,offlineMotion,replaying,inputFocused=true;float fps;double physicsMs;
        void Awake()
        {
            Application.targetFrameRate=60;QualitySettings.vSyncCount=0;QualitySettings.antiAliasing=0;Time.fixedDeltaTime=SlimeSimulation.StepTime;
            font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Segoe UI"},18);
            labelMaterial=new Material(Resources.Load<Material>("WorldLabel"));Font.textureRebuilt+=RefreshFont;
            stone=Mat(new Color(.42f,.46f,.43f),.18f);
            lightMaterial=new Material(Resources.Load<Material>("Glow"));lightMaterial.SetColor("_UnlitColor",new Color(.8f,.85f,.6f));lightMaterial.SetColor("_EmissiveColor",new Color(1,.96f,.69f)*2500);
            var cameraObject=new GameObject("Third person camera");view=cameraObject.AddComponent<Camera>();view.fieldOfView=50;view.nearClipPlane=.06f;view.farClipPlane=100;
            view.clearFlags=CameraClearFlags.Skybox;view.allowHDR=true;view.depthTextureMode=DepthTextureMode.Depth;cameraObject.AddComponent<AudioListener>();SlimeRendering.Configure(view,true);SlimeRendering.AddPostProcessing();
            view.rect=new Rect(0,0,1,1);rig=new SlimeFollowCamera(view);
            view.cullingMask=~(1<<9);
            var sun=new GameObject("Large daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(42,-35,0);sun.color=new Color(1,.98f,.95f);sun.intensity=55000;sun.shadows=LightShadows.Soft;
            var fill=new GameObject("Cool fill").AddComponent<Light>();fill.type=LightType.Directional;fill.transform.rotation=Quaternion.Euler(25,145,0);fill.color=new Color(.56f,.85f,1);fill.intensity=.35f;
            SlimeRendering.ConfigureSun(sun,fill);
            SlimeStreet.Build();
            Simulation=new SlimeSimulation();surface=new GameObject("Slime continuous volume").AddComponent<SlimeSurface>();
            motor=new SlimeMotor(Simulation);colony=new SlimeColony(Simulation,motor);
            environment=new RenderTexture(256,256,24,RenderTextureFormat.ARGBHalf){dimension=TextureDimension.Cube,useMipMap=true,autoGenerateMips=false};environment.Create();
            pendingEnvironment=new RenderTexture(environment.descriptor){name="Next liquid reflection",useMipMap=true,autoGenerateMips=false};pendingEnvironment.Create();
            environmentCamera=new GameObject("Reflection capture camera").AddComponent<Camera>();environmentCamera.enabled=false;environmentCamera.allowHDR=true;environmentCamera.clearFlags=CameraClearFlags.Skybox;environmentCamera.nearClipPlane=.05f;environmentCamera.farClipPlane=180;environmentCamera.cullingMask=~(1<<8);environmentCamera.transform.position=new Vector3(0,1.2f,0);
            SlimeRendering.Configure(environmentCamera,false);
            surface.Initialize(Simulation,Resources.Load<Material>("SlimeGlass"));
            spray=new GameObject("Secondary impact droplets").AddComponent<SlimeSpray>();spray.Initialize(Simulation,view);
            Select(Scenario.Course);UpdatePresentation(1f/60,true);
            var args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]=="-slimeCapture"){captureMode=true;StartCoroutine(Capture(args[i+1],true));}
            for(int i=0;i<args.Length-1;i++)if(args[i]=="-slimeOptics"){captureMode=true;StartCoroutine(Optics(args[i+1]));}
            for(int i=0;i<args.Length-1;i++)if(args[i]=="-slimeMotion"){captureMode=true;StartCoroutine(Motion(args[i+1]));}
            for(int i=0;i<args.Length-1;i++)if(args[i]=="-slimeBenchmark"){captureMode=true;StartCoroutine(Benchmark(args[i+1]));}
            for(int i=0;i<args.Length-1;i++)if(args[i]=="-slimeBatches"){captureMode=true;StartCoroutine(AuditBatches(args[i+1]));}
            for(int i=0;i<args.Length-1;i++)if(args[i]=="-slimeColony"){captureMode=true;StartCoroutine(CaptureColony(args[i+1]));}
            for(int i=0;i<args.Length-1;i++)if(args[i]=="-slimeCamera"){captureMode=true;StartCoroutine(AuditCamera(args[i+1]));}
            if(!captureMode)SetPointerCapture(true);
        }
        Material Mat(Color color,float smooth){var m=new Material(Resources.Load<Material>("Standard"));m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",smooth);return m;}
        GameObject Box(string name,Vector3 center,Vector3 size,Material m,bool collision=true)
        {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.layer=10;go.transform.position=center;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=m;if(!collision)Destroy(go.GetComponent<Collider>());return go;}
        public void Select(Scenario next)
        {
            foreach(var item in followerSurfaces.Values){item.gameObject.SetActive(false);Destroy(item.gameObject);}followerSurfaces.Clear();colony.Reset();
            scenario=next;auto=false;if(architecture!=null){architecture.SetActive(false);Destroy(architecture);}architecture=new GameObject("Test architecture");
            command=default;accumulator=0;
            Simulation.Solids.Clear();Simulation.Solids.AddRange(Arena.Create(next));Simulation.Cohesion=100;Simulation.Viscosity=.18f;
            foreach(var box in Simulation.Solids){var obj=Box(box.name,box.center,box.half*2,stone);obj.transform.SetParent(architecture.transform);}
            // 门窗边缘的亮色饰条便于直接判断是否从开口穿过。
            if(next==Scenario.Door||next==Scenario.Window)
            {
                float gap=next==Scenario.Door?.74f:.9f,low=next==Scenario.Window?.6f:0,high=next==Scenario.Window?2.1f:2.4f;
                Opening(2,gap,low,high);
            }
            if(next==Scenario.Course)
            {
                foreach(float x in new[]{6f,12f})Opening(x,x==6?.95f:1.1f,x==6?0:.65f,x==6?2.4f:2.1f,x==6?6:-6);
                Sign("低障碍  /  空格跳跃",new Vector3(-4,1.65f,-6.2f));Sign("平台  /  朝墙移动自动攀附",new Vector3(.3f,2.9f,7.35f));
                Sign("窄门  /  朝开口移动自动收窄",new Vector3(5.65f,3.1f,6));Sign("高窗  /  自动攀附与穿行",new Vector3(11.65f,3.1f,-6));
            }
            SlimeMeshBatches.Combine(architecture.transform,false);
            foreach(var collider in architecture.GetComponentsInChildren<Collider>())if(collider.name.StartsWith("Rail"))collider.gameObject.layer=SlimeFollowCamera.ThinObstacleLayer;
            Simulation.Solids.AddRange(Arena.District());
            Simulation.Reset(Arena.Spawn(next));motor.Reset(90);rig.Reset(Simulation.Center,90);spray.Clear();surface.ClearWetness();surface.Rebuild();Physics.SyncTransforms();RefreshEnvironment();
            status=next==Scenario.Course?"98 × 62 米街区：中央门窗、两侧栏杆、交叉街道和可攀爬屋顶。":"手动验收："+Arena.Names[(int)next]+"。Tab 展开场景面板。";
        }
        void Opening(float x,float gap,float low,float high,float offsetZ=0)
        {
            foreach(float face in new[]{x-.27f,x+.27f})
            {
                foreach(float z in new[]{-gap*.5f,gap*.5f})Box("Opening edge",new Vector3(face,(low+high)*.5f,z+offsetZ),new Vector3(.025f,high-low+.07f,.045f),lightMaterial,false).transform.SetParent(architecture.transform);
                foreach(float y in new[]{low,high})Box("Opening edge",new Vector3(face,y,offsetZ),new Vector3(.025f,.045f,gap+.07f),lightMaterial,false).transform.SetParent(architecture.transform);
            }
        }
        void Sign(string label,Vector3 position)
        {
            var go=new GameObject("Course sign");go.transform.SetParent(architecture.transform);go.transform.position=position;go.transform.rotation=Quaternion.Euler(0,90,0);
            var mesh=go.AddComponent<TextMesh>();mesh.text=label;mesh.font=font;mesh.fontSize=60;mesh.characterSize=.022f;mesh.anchor=TextAnchor.MiddleCenter;mesh.color=new Color(.88f,1,.92f);go.GetComponent<MeshRenderer>().sharedMaterial=labelMaterial;RefreshFont(font);
        }
        void RefreshFont(Font changed){if(changed==font&&labelMaterial!=null)labelMaterial.mainTexture=font.material.mainTexture;}
        void UpdatePresentation(float dt,bool snap=false)
        {
            // 镜头始终跟随主角；分身和融合不再改变取景中心或自动缩放。
            rig.Tick(Simulation.Center,dt,snap);UpdateEyes(surface,Simulation,motor);SyncFollowers();
            if(!environmentReady)RefreshEnvironment();
            else if(environmentFace>=0||Vector3.Distance(environmentCamera.transform.position,Simulation.Center+Vector3.up*.5f)>3.5f)
            {
                if(environmentFace<0){environmentCamera.transform.position=Simulation.Center+Vector3.up*.5f;environmentFace=0;}
                // 行进时每次只更新一个面，完整后交换，避免六次相机渲染造成周期性卡顿或半更新接缝。
                if(SlimeRendering.CaptureEnvironment(environmentCamera,pendingEnvironment,environmentFace,1)&&++environmentFace==6)
                {var previous=environment;environment=pendingEnvironment;pendingEnvironment=previous;environmentFace=-1;BindEnvironment();}
            }
        }
        void UpdateEyes(SlimeSurface skin,SlimeSimulation fluid,SlimeMotor control)
        {
            float scale=Mathf.Pow(fluid.ParticleCount/(float)SlimeSimulation.Count,1f/3);Vector3 front=control.Forward,right=Vector3.Cross(Vector3.up,front);
            Vector3 face=fluid.Center+front*(.55f*scale);face.y=fluid.Bounds.min.y+fluid.Bounds.size.y*.70f;skin.SetEyes(face-right*(.20f*scale),face+right*(.20f*scale),front);
        }
        void SyncFollowers()
        {
            retiredFollowers.Clear();foreach(var pair in followerSurfaces)if(pair.Key.ParticleCount==0)retiredFollowers.Add(pair.Key);
            foreach(var fluid in retiredFollowers){followerSurfaces[fluid].gameObject.SetActive(false);Destroy(followerSurfaces[fluid].gameObject);followerSurfaces.Remove(fluid);}
            foreach(var follower in colony.Followers)
            {
                if(!followerSurfaces.TryGetValue(follower.simulation,out var skin))
                {skin=new GameObject("Following small slime").AddComponent<SlimeSurface>();skin.Initialize(follower.simulation,Resources.Load<Material>("SlimeGlass"),surface.Wetness);skin.SetEnvironment(environment);followerSurfaces.Add(follower.simulation,skin);}
                UpdateEyes(skin,follower.simulation,follower.motor);
            }
        }
        void BindEnvironment(){surface.SetEnvironment(environment);spray.SetEnvironment(environment);foreach(var skin in followerSurfaces.Values)skin.SetEnvironment(environment);}
        void RefreshEnvironment(){environmentFace=-1;environmentCamera.transform.position=Simulation.Center+Vector3.up*.5f;environmentReady=SlimeRendering.CaptureEnvironment(environmentCamera,environment);BindEnvironment();}
        void Simulate(float dt){if(!captureMode||replaying||auto){colony.Tick(command,dt);command.jump=false;}else Simulation.Step(dt);spray.Step(dt);}
        void Tap(){Vector3 point=new Vector3(Simulation.Center.x,Simulation.Bounds.max.y,Simulation.Center.z);Simulation.Poke(point,Vector3.down*7,.65f);spray.Disturb(point,7);}
        void SetPointerCapture(bool locked){Cursor.lockState=locked?CursorLockMode.Locked:CursorLockMode.None;Cursor.visible=!locked;}
        void OnDestroy(){if(!captureMode)SetPointerCapture(false);Font.textureRebuilt-=RefreshFont;if(labelMaterial!=null)Destroy(labelMaterial);if(environment!=null){environment.Release();Destroy(environment);}if(pendingEnvironment!=null){pendingEnvironment.Release();Destroy(pendingEnvironment);}}
        void OnApplicationFocus(bool focused)
        {
            inputFocused=focused;if(!focused&&!captureMode){SetPointerCapture(false);command=default;auto=false;if(Simulation!=null&&Simulation.IsGrabbed)Simulation.EndGrab();}
        }
        void Update()
        {
            fps=Mathf.Lerp(fps,1/Mathf.Max(.001f,Time.unscaledDeltaTime),.04f);
            if(!captureMode&&inputFocused)
            {
                if(Input.GetKeyDown(KeyCode.R))Select(scenario);for(int i=0;i<Arena.Names.Length;i++)if(Input.GetKeyDown(KeyCode.Alpha1+i))Select((Scenario)i);
                if(Input.GetKeyDown(KeyCode.Tab)){panel=!panel;SetPointerCapture(!panel&&rig.Following);}
                if(Input.GetKeyDown(KeyCode.Escape)){SetPointerCapture(false);panel=false;auto=false;if(Simulation.IsGrabbed)Simulation.EndGrab();}
                if(Input.GetKeyDown(KeyCode.V)){rig.Following=!rig.Following;SetPointerCapture(rig.Following&&!panel);}
                if(Input.GetKeyDown(KeyCode.X))rig.SwapShoulder();
                if(Input.GetMouseButtonDown(0)&&!panel&&rig.Following&&Cursor.lockState!=CursorLockMode.Locked)SetPointerCapture(true);
                if(Cursor.lockState==CursorLockMode.Locked)rig.Orbit(new Vector2(Input.GetAxis("Mouse X"),Input.GetAxis("Mouse Y")));
                rig.Aiming=Cursor.lockState==CursorLockMode.Locked&&Input.GetMouseButton(1);
                rig.Distance=Mathf.Clamp(rig.Distance-Input.mouseScrollDelta.y*.4f,3.5f,8);if(!rig.Following)HandlePointer();
                if(Input.GetKeyDown(KeyCode.F))Tap();
                if(Input.GetKeyDown(KeyCode.Q))SplitSlime();if(Input.GetKeyDown(KeyCode.G)){colony.Recall();status="小史莱姆正在沿路径回来，接触后合并。";}
                command.move=panel?Vector2.zero:new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));command.cameraYaw=rig.Yaw;
                command.sprint=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);command.jump|=Input.GetKeyDown(KeyCode.Space);
                if(command.move.sqrMagnitude>0||command.jump)auto=false;
            }
            if(auto)AutoDrive();
            var watch=System.Diagnostics.Stopwatch.StartNew();if(!offlineMotion){accumulator+=Mathf.Min(Time.deltaTime,.05f);while(accumulator>=SlimeSimulation.StepTime){Simulate(SlimeSimulation.StepTime);accumulator-=SlimeSimulation.StepTime;}}physicsMs=watch.Elapsed.TotalMilliseconds;
            if(checking!=null&&checking.IsCompleted){if(checking.IsFaulted)status=checking.Exception.GetBaseException().Message;else{lastReport=checking.Result;status=lastReport.passed?"全部回归检查通过。":"存在失败项，请看测试报告。";File.WriteAllText(Path.Combine(Application.persistentDataPath,"Validation.json"),JsonUtility.ToJson(lastReport,true));}checking=null;}
        }
        void LateUpdate(){if(!offlineMotion)UpdatePresentation(Time.deltaTime);}
        void HandlePointer()
        {
            bool overUi=(panel&&Input.mousePosition.x>Screen.width-330*uiScale)||Input.mousePosition.y>Screen.height-64*uiScale||Input.mousePosition.y<80*uiScale;
            Ray ray=view.ScreenPointToRay(Input.mousePosition);
            if(Input.GetMouseButtonDown(0)&&!overUi)
            {
                float closest=float.MaxValue;int index=-1;
                for(int i=0;i<Simulation.ParticleCount;i++){Vector3 d=Simulation.Position[i]-ray.origin;float t=Vector3.Dot(d,ray.direction);if(t>0&&(d-ray.direction*t).magnitude<.23f&&t<closest){closest=t;index=0;}}
                if(index>=0&&(!Physics.Raycast(ray,out RaycastHit hit)||hit.distance>closest-.1f))
                {Vector3 point=ray.GetPoint(closest);Simulation.BeginGrab(point);grabPlane=new Plane(-view.transform.forward,Simulation.GrabTarget);auto=false;pressScreen=Input.mousePosition;pressTime=Time.time;tapPoint=point;}
            }
            if(Input.GetMouseButton(0)&&Simulation.IsGrabbed&&grabPlane.Raycast(ray,out float enter))Simulation.GrabTarget=ray.GetPoint(enter);
            if(Input.GetMouseButtonUp(0)&&Simulation.IsGrabbed){Simulation.EndGrab();if(Time.time-pressTime<.25f&&(Input.mousePosition-pressScreen).sqrMagnitude<36){Simulation.Poke(tapPoint,view.transform.forward*2+Vector3.down*3);spray.Disturb(tapPoint,3);}}
        }
        void AutoDrive(){command=new SlimeInput{move=Vector2.up,cameraYaw=motor.FacingYaw};}
        void SplitSlime(){if(colony.Split()){spray.Clear();status="已分裂：可继续按 Q 细分，分身独立跟随，G 集合融合。";}else status=Simulation.IsGrabbed?"释放提拉后可以继续分裂。":"已达到粒子最小体积，合并后可继续细分。";}
        void Styles()
        {
            if(title!=null)return;title=new GUIStyle(GUI.skin.label){font=font,fontSize=25,fontStyle=FontStyle.Bold};title.normal.textColor=new Color(.87f,.96f,.88f);
            text=new GUIStyle(GUI.skin.label){font=font,fontSize=16,wordWrap=true};text.normal.textColor=new Color(.80f,.88f,.85f);
            small=new GUIStyle(text){fontSize=13};button=new GUIStyle(GUI.skin.button){font=font,fontSize=15,fixedHeight=39};
            active=new GUIStyle(button);active.normal.textColor=new Color(.62f,1,.57f);card=new GUIStyle(GUI.skin.box){padding=new RectOffset(17,17,14,14)};
        }
        void OnGUI()
        {
            if(batchAudit)return;
            Styles();uiScale=Mathf.Clamp(Screen.height/900f,.75f,1.4f);GUI.matrix=Matrix4x4.Scale(Vector3.one*uiScale);float width=Screen.width/uiScale,height=Screen.height/uiScale;
            GUI.Box(new Rect(16,14,415,61),GUIContent.none);GUI.Label(new Rect(30,21,400,31),"SLIME  /  "+Arena.Names[(int)scenario],title);
            GUI.Label(new Rect(30,52,400,23),(rig.Following?"TPS 肩后视角":"自由观察 / 左键提拉")+"  ·  "+motor.State+"  ·  Tab 场景面板",small);
            if(GUI.Button(new Rect(width-155,20,135,37),panel?"收起面板  Tab":"场景与设置  Tab",button)){panel=!panel;if(!captureMode)SetPointerCapture(!panel&&rig.Following);}
            if(rig.Following&&!panel){Color prior=GUI.color;GUI.color=new Color(.9f,1,.95f,.8f);GUI.DrawTexture(new Rect(width*.5f-7,height*.5f,14,1),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(width*.5f,height*.5f-7,1,14),Texture2D.whiteTexture);GUI.color=prior;}
            GUI.Box(new Rect(16,height-88,panel?width-370:Mathf.Min(width-32,1000),72),GUIContent.none);
            GUI.Label(new Rect(30,height-82,panel?width-400:width-60,48),"WASD 移动，自动识别攀附与窄缝  ·  Shift 加速  ·  空格 跳跃  ·  Q 分裂  ·  G 集合\n鼠标 环视 · 右键 瞄准 · X 换肩 · 滚轮 距离 · Esc 释放鼠标 · V 观察/提拉 · R 重置",small);
            GUI.Label(new Rect(30,height-121,panel?width-400:width-60,30),status,small);
            if(!panel)return;
            GUILayout.BeginArea(new Rect(width-326,78,310,height-96),card);
            GUILayout.Label("测试场景",title);
            if(GUILayout.Button("主动分裂  Q",button))SplitSlime();if(GUILayout.Button("召回并合并  G",button))colony.Recall();GUILayout.Space(10);
            for(int i=0;i<Arena.Names.Length;i++)if(GUILayout.Button((i+1)+"   "+Arena.Names[i],scenario==(Scenario)i?active:button))Select((Scenario)i);
            GUILayout.Space(14);
            if(GUILayout.Button(scenario==Scenario.Rest||scenario==Scenario.Course?"轻触水面   F":auto?"停止自动演示":"自动演示本项",button)){if(scenario==Scenario.Rest||scenario==Scenario.Course)Tap();else{auto=!auto;Simulation.Wake();}}
            if(GUILayout.Button("重置本场景   R",button))Select(scenario);
            GUILayout.Space(16);GUILayout.Label("材质",text);
            GUILayout.Label("凝聚力  "+Simulation.Cohesion.ToString("F0"),small);float c=GUILayout.HorizontalSlider(Simulation.Cohesion,30,250);if(Mathf.Abs(c-Simulation.Cohesion)>.01f){Simulation.Cohesion=c;Simulation.Wake();}
            GUILayout.Label("黏度  "+Simulation.Viscosity.ToString("F2"),small);float v=GUILayout.HorizontalSlider(Simulation.Viscosity,.05f,.7f);if(Mathf.Abs(v-Simulation.Viscosity)>.001f){Simulation.Viscosity=v;Simulation.Wake();}
            GUILayout.Space(18);if(GUILayout.Button(checking==null?"运行回归测试":"正在检查…",button)&&checking==null){checking=Task.Run(()=>SlimeChecks.Run());status="正在独立模拟测试用例，当前场景可继续操作。";}
            showMetrics=GUILayout.Toggle(showMetrics,"显示测量值");
            bool detail=GUILayout.Toggle(highDetail,"精细表面（性能开销更高）");if(detail!=highDetail){highDetail=detail;surface.GridSpacing=detail?.04f:.055f;surface.Rebuild();}
            if(showMetrics){GUILayout.Label($"个体  {colony.Followers.Count+1} · 粒子  {colony.TotalParticles}  ·  {fps:F0} FPS\n状态  {(Simulation.Sleeping?"静止 / 休眠":"运动中")}\nRMS  {Simulation.RmsSpeed:F4} m/s\n实体间隙  {Simulation.MinClearance():F4} m",small);}
            if(!captureMode&&GUILayout.Button("截图验收演示",button)){captureMode=true;StartCoroutine(Capture(Path.Combine(Application.dataPath,"../Captures")));}
            if(lastReport!=null){int passed=lastReport.checks.FindAll(x=>x.passed).Count;GUILayout.Label($"测试  {passed}/{lastReport.checks.Count}",text);foreach(var r in lastReport.checks)if(!r.passed)GUILayout.Label("× "+r.name,small);}
            GUILayout.EndArea();
        }
        void CaptureFrame(string path)
        {
            // 原生离屏相机渲染使隐藏窗口的自动验收也有真实图像，避免依赖交换链。
            var target=new RenderTexture(1440,900,24,RenderTextureFormat.ARGB32);target.antiAliasing=1;target.Create();
            var prior=RenderTexture.active;Rect rect=view.rect;view.rect=new Rect(0,0,1,1);view.targetTexture=target;spray.UpdateMesh();var renderWatch=System.Diagnostics.Stopwatch.StartNew();SlimeRendering.Render(view);
            RenderTexture.active=target;var pixels=new Texture2D(1440,900,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1440,900),0,0);pixels.Apply();
            renderWatch.Stop();
            File.WriteAllBytes(path,pixels.EncodeToPNG());view.targetTexture=null;view.rect=rect;RenderTexture.active=prior;target.Release();Destroy(target);Destroy(pixels);
            Debug.Log($"[SLIME-CAPTURE] {Path.GetFileName(path)}: center={Simulation.Center}; sleep={Simulation.Sleeping}; rms={Simulation.RmsSpeed:F6}; update fps={fps:F1}; physics={physicsMs:F2} ms; volume submit CPU={surface.LastRebuildMs:F2} ms; render and readback={renderWatch.Elapsed.TotalMilliseconds:F2} ms; particle clearance={Simulation.MinClearance():F6}");
            if(!offlineMotion)Debug.Log($"[SLIME-GPU] {Path.GetFileName(path)}: positive density voxels inside solids={surface.SolidVolumeViolations()}");
            Debug.Log($"[SLIME-SPRAY] {Path.GetFileName(path)}: active={spray.ActiveCount}; emitted={spray.TotalEmitted}");
            Debug.Log($"[SLIME-CAMERA] {Path.GetFileName(path)}: follow error={Vector3.Distance(rig.Focus,Simulation.Center+Vector3.up*SlimeFollowCamera.TargetHeight):F4}; boom={rig.ActualDistance:F3}; obstructed={rig.Obstructed}; inside solid={Physics.CheckSphere(view.transform.position,.10f,SlimeFollowCamera.ObstacleMask,QueryTriggerInteraction.Ignore)}; facing={motor.FacingYaw:F1}; state={motor.State}");
            Debug.Log($"[SLIME-SENSE] {Path.GetFileName(path)}: climbing={motor.Climbing}; squeezing={motor.Squeezing}");
            Debug.Log($"[SLIME-POSE] {Path.GetFileName(path)}: position={view.transform.position.ToString("G9")}; rotation={view.transform.rotation.ToString("G9")}; fov={view.fieldOfView:G9}; revision={Simulation.Revision}; environment={environmentCamera.transform.position.ToString("G9")}");
        }
        IEnumerator Optics(string folder)
        {
            Directory.CreateDirectory(folder);yield return new WaitForSeconds(1);
            for(int i=0;i<=5;i++){Shader.SetGlobalFloat("_SlimeDebug",i);yield return null;CaptureFrame(Path.Combine(folder,"optics-"+i+".png"));}
            Simulation.BeginGrab(new Vector3(Simulation.Center.x,Simulation.Bounds.max.y,Simulation.Center.z));Vector3 anchor=Simulation.GrabTarget;Shader.SetGlobalFloat("_SlimeDebug",0);
            for(float t=0;t<1.5f;t+=Time.deltaTime){Simulation.GrabTarget=anchor+new Vector3(.55f,1.65f,0)*Mathf.Clamp01(t/1.5f);yield return null;}
            CaptureFrame(Path.Combine(folder,"lift.png"));Application.Quit();
        }
        IEnumerator Motion(string folder)
        {
            Directory.CreateDirectory(folder);Select(Scenario.Course);yield return new WaitForSeconds(1);offlineMotion=true;replaying=true;rig.Following=true;
            for(int frame=0;frame<900;frame++)
            {
                command=new SlimeInput{cameraYaw=rig.Yaw};
                if(frame>=45&&frame<180)command.move=Vector2.up;
                if(frame>=120&&frame<180)command.sprint=true;
                if(frame==180)command.jump=true;
                if(frame>=180&&frame<240)command.move=Vector2.up;
                if(frame>=240&&frame<285)command.move=Vector2.right;
                if(frame>=300&&frame<345)rig.Yaw+=4;
                if(frame==360){Select(Scenario.Door);rig.Yaw=-90;UpdatePresentation(1f/30,true);}
                if(frame==390)rig.Yaw=90;
                if(frame>=390&&frame<540&&Simulation.Bounds.min.x<2.5f){command.cameraYaw=rig.Yaw;command.move=Vector2.up;}
                if(frame==540)Select(Scenario.Climb);
                if(frame>=540&&frame<690&&Simulation.Center.x<3.6f){command.cameraYaw=rig.Yaw;command.move=Vector2.up;}
                if(frame==720)Select(Scenario.Window);
                if(frame>=720&&frame<870&&Simulation.Bounds.min.x<2.5f){command.cameraYaw=rig.Yaw;command.move=Vector2.up;}
                for(int step=0;step<3;step++)Simulate(SlimeSimulation.StepTime);
                surface.Rebuild();yield return null;UpdatePresentation(1f/30);CaptureFrame(Path.Combine(folder,frame.ToString("D4")+".png"));
            }
            Application.Quit();
        }
        IEnumerator Capture(string folder,bool quit=false)
        {
            Directory.CreateDirectory(folder);Select(Scenario.Rest);rig.Yaw=-76;rig.Pitch=12;UpdatePresentation(1f/60,true);yield return new WaitForSeconds(1);CaptureFrame(Path.Combine(folder,"warmup.png"));
            yield return new WaitForSeconds(.5f);CaptureFrame(Path.Combine(folder,"Unity-Slime-rest.png"));
            yield return new WaitForSeconds(3);CaptureFrame(Path.Combine(folder,"Unity-Slime-rest-later.png"));
            yield return new WaitForSeconds(.3f);Simulation.BeginGrab(new Vector3(Simulation.Center.x,Simulation.Bounds.max.y,Simulation.Center.z));Vector3 anchor=Simulation.GrabTarget;
            for(float t=0;t<1.5f;t+=Time.deltaTime){Simulation.GrabTarget=anchor+new Vector3(.55f,1.65f,0)*Mathf.Clamp01(t/1.5f);yield return null;}
            CaptureFrame(Path.Combine(folder,"Unity-Slime-lift.png"));yield return new WaitForSeconds(.3f);Simulation.EndGrab();yield return new WaitForSeconds(4);
            CaptureFrame(Path.Combine(folder,"Unity-Slime-release.png"));
            Tap();yield return new WaitForSeconds(.12f);CaptureFrame(Path.Combine(folder,"Unity-Slime-touch.png"));yield return new WaitForSeconds(.25f);CaptureFrame(Path.Combine(folder,"Unity-Slime-splash.png"));yield return new WaitForSeconds(3);
            yield return new WaitForSeconds(.3f);Select(Scenario.Wall);auto=true;yield return new WaitForSeconds(4);CaptureFrame(Path.Combine(folder,"Unity-Slime-wall.png"));
            yield return new WaitForSeconds(.3f);Select(Scenario.Climb);auto=true;yield return new WaitForSeconds(5);CaptureFrame(Path.Combine(folder,"Unity-Slime-climb.png"));
            yield return new WaitForSeconds(.3f);Select(Scenario.Door);auto=true;yield return new WaitForSeconds(.9f);CaptureFrame(Path.Combine(folder,"Unity-Slime-door.png"));
            yield return new WaitForSeconds(6);CaptureFrame(Path.Combine(folder,"Unity-Slime-door-exit.png"));
            yield return new WaitForSeconds(.3f);Select(Scenario.Window);auto=true;yield return new WaitForSeconds(1.1f);CaptureFrame(Path.Combine(folder,"Unity-Slime-window.png"));
            yield return new WaitForSeconds(6);CaptureFrame(Path.Combine(folder,"Unity-Slime-window-exit.png"));
            yield return new WaitForSeconds(1);captureMode=false;Select(Scenario.Rest);status="验收截图已保存："+Path.GetFullPath(folder);if(quit)Application.Quit();
        }
    }
}





