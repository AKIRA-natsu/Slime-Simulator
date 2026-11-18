using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace SlimeLab
{
    // 真正合并同材质几何；不依赖多 Pass 下仍会拆分的静态批处理。
    // 原物体仅保留碰撞体和 Transform，确保镜头及实体碰撞不因渲染优化改变。
    public sealed class SlimeMeshBatches:MonoBehaviour
    {
        readonly List<Mesh> ownedMeshes=new List<Mesh>();
        public int SourceRenderers {get;private set;}
        public int MergedRenderers {get;private set;}
        public long SourceTriangles {get;private set;}
        public long MergedTriangles {get;private set;}
        public bool CollidersUnchanged {get;private set;}
        public static string ColliderSignature(GameObject root)
        {
            var items=new List<string>();foreach(var c in root.GetComponentsInChildren<Collider>())
            {
                Bounds b=c.bounds;items.Add(c.GetType().Name+"|"+c.gameObject.layer+"|"+c.enabled+"|"+b.center.ToString("F5")+"|"+b.size.ToString("F5"));
            }
            items.Sort(StringComparer.Ordinal);using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",items)))).Replace("-","");
        }
        public static SlimeMeshBatches Combine(Transform root,bool splitStreetSides)
        {
            var owner=root.gameObject.AddComponent<SlimeMeshBatches>();string prior=ColliderSignature(root.gameObject);
            var groups=new Dictionary<(Material,int,int),List<MeshFilter>>();
            var mergedObjects=new List<GameObject>();
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var filter=renderer.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null||filter.sharedMesh.subMeshCount!=1||renderer.sharedMaterial==null)continue;
                int side=splitStreetSides?(renderer.bounds.center.z<-.01f?-1:renderer.bounds.center.z>.01f?1:0):0;
                var key=(renderer.sharedMaterial,side,renderer.gameObject.layer);if(!groups.TryGetValue(key,out var list)){list=new List<MeshFilter>();groups.Add(key,list);}list.Add(filter);
                owner.SourceRenderers++;owner.SourceTriangles+=(long)filter.sharedMesh.GetIndexCount(0)/3;
            }
            foreach(var pair in groups)
            {
                var instances=new CombineInstance[pair.Value.Count];int vertices=0;
                for(int i=0;i<instances.Length;i++){var filter=pair.Value[i];vertices+=filter.sharedMesh.vertexCount;instances[i]=new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=0,transform=root.worldToLocalMatrix*filter.transform.localToWorldMatrix};}
                var mesh=new Mesh{name="Merged "+pair.Key.Item1.name+" / "+pair.Key.Item2,indexFormat=vertices>65535?IndexFormat.UInt32:IndexFormat.UInt16};
                mesh.CombineMeshes(instances,true,true);mesh.RecalculateBounds();owner.ownedMeshes.Add(mesh);owner.MergedTriangles+=(long)mesh.GetIndexCount(0)/3;
                var go=new GameObject(mesh.name);go.layer=pair.Key.Item3;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=pair.Key.Item1;owner.MergedRenderers++;
                mergedObjects.Add(go);
            }
            if(owner.SourceTriangles!=owner.MergedTriangles)throw new InvalidOperationException("Mesh merge lost triangles");
            // 多光源 Pass 只需提交合并后的少量网格，深度和阴影 Pass 仍可利用静态批处理。
            if(mergedObjects.Count>0)StaticBatchingUtility.Combine(mergedObjects.ToArray(),root.gameObject);
            // 静态批处理也会生成共享网格，切换场景时一并释放。
            foreach(var go in mergedObjects){var mesh=go.GetComponent<MeshFilter>().sharedMesh;if(!owner.ownedMeshes.Contains(mesh))owner.ownedMeshes.Add(mesh);}
            foreach(var mesh in owner.ownedMeshes)if(mesh.isReadable)mesh.UploadMeshData(true);
            foreach(var group in groups.Values)foreach(var filter in group){var renderer=filter.GetComponent<MeshRenderer>();renderer.enabled=false;Destroy(renderer);Destroy(filter);}
            owner.CollidersUnchanged=prior==ColliderSignature(root.gameObject);if(!owner.CollidersUnchanged)throw new InvalidOperationException("Mesh merge changed colliders");
            Debug.Log($"[SLIME-MESH] {root.name}: renderers {owner.SourceRenderers}->{owner.MergedRenderers}; triangles {owner.SourceTriangles}->{owner.MergedTriangles}; colliders unchanged={owner.CollidersUnchanged}");return owner;
        }
        void OnDestroy(){foreach(var mesh in ownedMeshes)if(mesh!=null)Destroy(mesh);}
    }
}
