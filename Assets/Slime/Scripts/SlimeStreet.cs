using UnityEngine;
namespace SlimeLab
{
    public static class SlimeStreet
    {
        static Transform root;
        static Material Mat(Color c,float smooth=.25f){var m=new Material(Resources.Load<Material>("Standard")){name="Street "+ColorUtility.ToHtmlStringRGB(c)};m.SetColor("_BaseColor",c);m.SetFloat("_Smoothness",smooth);return m;}
        static GameObject Box(string name,Vector3 p,Vector3 scale,Material m,bool collision=false)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.layer=10;g.transform.position=p;g.transform.localScale=scale;g.transform.SetParent(root,true);g.GetComponent<Renderer>().sharedMaterial=m;
            if(!collision){var collider=g.GetComponent<Collider>();collider.enabled=false;Object.Destroy(collider);}return g;
        }
        public static void Build()
        {
            root=new GameObject("Static street geometry").transform;
            var asphalt=new Material(Resources.Load<Material>("Street"));
            var paving=new Material(asphalt);paving.SetColor("_BaseColor",new Color(.8f,.77f,.7f));paving.SetFloat("_TexWorldScale",.7f);
            Box("Asphalt and wet contact",new Vector3(0,-.12f,0),new Vector3(100,.24f,64),asphalt,true);
            var cycle=new Material(asphalt);cycle.SetColor("_BaseColor",new Color(.35f,.75f,.43f));Box("Cycle lane",new Vector3(0,.002f,0),new Vector3(98,.004f,2.2f),cycle);
            var stone=Mat(new Color(.55f,.56f,.55f));var paint=Mat(new Color(.75f,.76f,.74f));
            var metal=Mat(new Color(.11f,.16f,.17f),.55f);var glass=Mat(new Color(.18f,.34f,.40f),.92f);glass.SetFloat("_Metallic",.65f);
            var brick=new Material(Resources.Load<Material>("Facade"));
            var awning=Mat(new Color(.025f,.17f,.12f));var terracotta=Mat(new Color(.30f,.12f,.065f));
            foreach(var solid in Arena.District())Box(solid.name,solid.center,solid.half*2,solid.name.EndsWith("paving")?paving:solid.name.EndsWith("building")?brick:solid.name=="Planter"?terracotta:solid.name=="Street lamp"||solid.name=="Lamp hood"?metal:stone,true);
            foreach(float z in new[]{-19f,19f})foreach(float x in new[]{-34f,-12f,12f,34f})
            {
                float sign=Mathf.Sign(z),face=z-sign*5.56f,h=Mathf.Abs(x)>20?5.2f:4.6f;
                Box("Cornice",new Vector3(x,h, z),new Vector3(14.3f,.16f,11.3f),stone);
                Box("Wall foundation",new Vector3(x,.22f,face),new Vector3(14,.32f,.12f),stone);
                for(int col=-2;col<=2;col++)
                {
                    Box("Window surround",new Vector3(x+col*2.6f,h-1.2f,face),new Vector3(1.55f,1.55f,.14f),stone);
                    Box("Window glass",new Vector3(x+col*2.6f,h-1.2f,face-sign*.08f),new Vector3(1.35f,1.35f,.04f),glass);
                    Box("Window mullion",new Vector3(x+col*2.6f,h-1.2f,face-sign*.12f),new Vector3(.035f,1.36f,.04f),metal);
                }
                foreach(float shop in new[]{-3.5f,3.5f})
                {
                    Box("Shop window",new Vector3(x+shop,1.3f,face),new Vector3(5.9f,2.2f,.08f),glass);
                    Box("Shop canopy",new Vector3(x+shop,2.5f,face-sign*.35f),new Vector3(6.1f,.22f,1.2f),awning);
                    for(int bar=-2;bar<=2;bar++)Box("Shop mullion",new Vector3(x+shop+bar*1.1f,1.3f,face-sign*.08f),new Vector3(.04f,2.2f,.06f),metal);
                }
                Box("Rain pipe",new Vector3(x+6.6f,h*.5f,face-sign*.15f),new Vector3(.09f,h,.09f),metal);
                Box("Air conditioner",new Vector3(x-5.8f,.8f,face-sign*.35f),new Vector3(1,.65f,.55f),paint);
            }
            // 交叉道路与斑马线提供远近尺度，场景细节共享材质并合并网格。
            for(int x=-44;x<=44;x+=4)Box("Lane marking",new Vector3(x,.006f,-.08f),new Vector3(1.9f,.008f,.10f),paint);
            foreach(float crossing in new[]{-23f,0,23f})for(int z=-9;z<=9;z+=2)Box("Crosswalk",new Vector3(crossing,.007f,z),new Vector3(3.2f,.009f,.85f),paint);
            SlimeMeshBatches.Combine(root,true);
        }
    }
}
