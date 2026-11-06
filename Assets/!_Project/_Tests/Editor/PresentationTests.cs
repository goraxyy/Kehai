using System.Collections;
using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

// Karen 0.2.0, phase 3: what the player sees of a throw and feels of its effect (B3.1, B3.2),
// written by the second builder. Art only: no colliders, no change to the seeded RNG, all cleaned up.

public class PresentationTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    [Test] public void ThrowModelsAreExistingCatalogueProducts()
    {
        foreach (ThrownItem item in System.Enum.GetValues(typeof(ThrownItem)))
        {
            string id=ThrownItemVisual.ProductId(item);
            Assert.IsNotNull(ProductCatalog.Get(id));
            Assert.IsTrue(ProductLook.For(id).HasValue,"the imported model is missing: "+id);
        }
    }
    [UnityTest] public IEnumerator ScreenEffectsClearWithoutCameraDriftOrChangingBurnoutProfile()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        yield return new EnterPlayMode();
        var player=new GameObject("visual test player");
        var camera=new GameObject("test camera"); camera.transform.SetParent(player.transform,false);
        camera.transform.localPosition=new Vector3(0,1.6f,0);
        var view=camera.AddComponent<Camera>();
        var motor=player.AddComponent<PlayerMotor>(); motor.enabled=false;
        motor.cameraRoot=camera.transform;
        var state=player.GetComponent<PlayerEffects>();
        var fx=player.AddComponent<PlayerScreenFx>();
        var data=view.GetUniversalAdditionalCameraData(); data.renderPostProcessing=false;
        var origin=camera.transform.localPosition;
        try
        {
            state.Blur(.15f); state.Stun(.15f);
            typeof(PlayerScreenFx).GetMethod("Read",Flags).Invoke(fx,new object[]{state});
            yield return null;
            var volume=camera.GetComponentInChildren<Volume>();
            Assert.IsNotNull(volume); Assert.IsFalse(volume.isGlobal); Assert.AreEqual(1f,volume.weight);
            Assert.IsTrue(data.renderPostProcessing);
            Assert.IsTrue(volume.sharedProfile.TryGet<DepthOfField>(out var blur));
            Assert.AreEqual(DepthOfFieldMode.Gaussian,blur.mode.value);
            float expires=Time.time+.2f;
            float deadline=Time.realtimeSinceStartup+15f;
            while(Time.time<expires && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.That(Time.time,Is.GreaterThanOrEqualTo(expires));
            yield return null;
            Assert.AreEqual(0f,volume.weight); Assert.IsFalse(data.renderPostProcessing);
            Assert.That(Vector3.Distance(origin,camera.transform.localPosition),Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(Quaternion.identity,camera.transform.localRotation),Is.LessThan(.01f));
            state.Blur(4); yield return null;
            Assert.IsTrue(data.renderPostProcessing);
            fx.enabled=false;
            Assert.IsFalse(data.renderPostProcessing); Assert.AreEqual(0f,volume.weight);
            Assert.That(Vector3.Distance(origin,camera.transform.localPosition),Is.LessThan(.0001f));
        }
        finally { Object.Destroy(player); }
        yield return null;
        yield return new ExitPlayMode();
    }
    [UnityTest] public IEnumerator ThrowArtFollowsPhysicsAndLeavesNoLiveColliderOrRandomStateChange()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        yield return new EnterPlayMode();
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position=new Vector3(0,-.1f,0); floor.transform.localScale=new Vector3(20,.2f,20);
        var projectile=new GameObject("test projectile").AddComponent<Projectile>(); projectile.enabled=false;
        projectile.transform.position=new Vector3(0,1,0);
        try
        {
            var e=new ThrowEvent{Item=ThrownItem.Tin,From=projectile.transform.position,Projectile=projectile};
            var random=Random.state;
            typeof(ThrownItemVisual).GetMethod("Thrown",Flags).Invoke(null,new object[]{e});
            Assert.AreEqual(random,Random.state);
            Assert.IsNotNull(projectile.GetComponentInChildren<MeshRenderer>());
            Assert.IsEmpty(projectile.GetComponentsInChildren<Collider>());
            projectile.transform.position=new Vector3(2,1,0);
            float reached=Time.time+.12f;
            float deadline=Time.realtimeSinceStartup+15f;
            while(Time.time<reached && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.That(Time.time,Is.GreaterThanOrEqualTo(reached));
            yield return null;
            Assert.That(Vector3.Distance(projectile.transform.position,projectile.transform.GetChild(0).position),Is.LessThan(.001f));
            foreach(ThrownItem item in System.Enum.GetValues(typeof(ThrownItem)))
            {
                e.Item=item; e.At=new Vector3((int)item*2,1,0);
                typeof(ThrownItemVisual).GetMethod("Landed",Flags).Invoke(null,new object[]{e});
            }
            yield return null;
            foreach(var effect in Object.FindObjectsByType<ThrownItemVisual>())
                foreach(var collider in effect.GetComponentsInChildren<Collider>()) Assert.IsFalse(collider.enabled);
            Assert.AreEqual(random,Random.state,"presentation must not perturb the seeded AI run");
            float cleaned=Time.time+5.2f;
            deadline=Time.realtimeSinceStartup+30f;
            while(Time.time<cleaned && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.That(Time.time,Is.GreaterThanOrEqualTo(cleaned));
            yield return null;
            Assert.AreEqual(1,Object.FindObjectsByType<ThrownItemVisual>().Length,"impact effects should clean themselves up");
        }
        finally { Object.Destroy(projectile.gameObject); Object.Destroy(floor); }
        yield return null;
        yield return new ExitPlayMode();
    }
}
