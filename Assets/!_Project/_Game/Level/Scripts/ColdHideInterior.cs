using UnityEngine;

// Only the view from inside fades the shell. Physical walls remain opaque to AI raycasts.
// No teleport, invulnerability or movement lock: walk in, close the door and walk back out.
public sealed class ColdHideInterior : MonoBehaviour
{
    public Bounds interior = new Bounds(new Vector3(0,1.25f,0),new Vector3(1.58f,2.35f,1.85f));
    public Material insideMaterial;
    Renderer[] shells;
    Material[][] originals;
    bool faded;
    void Awake()
    {
        shells=GetComponentsInChildren<Renderer>();originals=new Material[shells.Length][];
        for(int i=0;i<shells.Length;i++)originals[i]=shells[i].sharedMaterials;
    }
    void LateUpdate()
    {
        var player=PlayerInteract.Local;
        bool inside=player!=null&&interior.Contains(transform.InverseTransformPoint(player.transform.position+Vector3.up));
        if(inside==faded||insideMaterial==null)return;
        SetFaded(inside);
    }
    void SetFaded(bool value)
    {
        faded=value;
        for(int i=0;i<shells.Length;i++)
        {
            if(shells[i]==null)continue;
            if(!value){shells[i].sharedMaterials=originals[i];continue;}
            var mats=new Material[originals[i].Length];for(int m=0;m<mats.Length;m++)mats[m]=insideMaterial;
            shells[i].sharedMaterials=mats;
        }
    }
    void OnDisable(){if(faded&&shells!=null)SetFaded(false);}
}
