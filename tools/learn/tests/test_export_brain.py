"""C5.7 / C2.6: the export turns the trainer's actor into numbers the C# runs identically."""
import json
import os

import numpy as np
import pytest

torch = pytest.importorskip("torch")
pytest.importorskip("mlagents")
import export_brain as eb  # noqa: E402
import make_brain_fixture as fx  # noqa: E402


def test_our_arithmetic_matches_the_trainer(tmp_path):
    actor = fx.tiny_actor(seed=5)
    r = eb.export(actor.state_dict(), "Probe", {"version": "t", "outputs": [["a"] * 4, ["b"] * 3]}, str(tmp_path))
    assert r["parity"] < 1e-5
    cs = (tmp_path / "Probe.g.cs").read_text()
    assert "public static class Probe" in cs and "Kehai.Karen.BrainNet Build()" in cs
    part = json.loads((tmp_path / "Probe.part.json").read_text())
    assert part["kind"] == "brain" and len(part["heads"]) == 2
    assert "Parity with the trainer" in (tmp_path / "Probe.card.md").read_text()


def test_a_wrong_outputs_file_is_refused(tmp_path):
    actor = fx.tiny_actor(seed=5)
    with pytest.raises(SystemExit):
        eb.export(actor.state_dict(), "Probe", {"outputs": [["a"] * 5, ["b"] * 3]}, str(tmp_path))
    with pytest.raises(SystemExit):
        eb.export(actor.state_dict(), "Probe", {"input_size": 99}, str(tmp_path))
    assert not os.path.exists(tmp_path / "Probe.g.cs"), "nothing written when it's refused"


def test_karen_model_version_is_shared_by_all_three_export_files(tmp_path):
    actor = fx.tiny_actor(seed=5)
    name = "KarenBrainBasic"
    eb.export(actor.state_dict(), name, {"version": "practice-run", "model_version": "1.3.2"}, str(tmp_path))
    assert 'ModelVersion = "1.3.2"' in (tmp_path / (name + ".g.cs")).read_text()
    model_card = (tmp_path / (name + ".card.md")).read_text()
    assert "# Model card: Karen Satori" in model_card
    assert "| Version | 1.3.2 |" in model_card
    card = json.loads((tmp_path / (name + ".part.json")).read_text())["card"]
    assert card["version"] == "practice-run" and card["model_version"] == "1.3.2"


def test_a_checkpoint_on_disk_round_trips(tmp_path):
    actor = fx.tiny_actor(seed=8)
    run = tmp_path / "run" / "Employee"
    run.mkdir(parents=True)
    torch.save({"Policy": actor.state_dict()}, run / "checkpoint.pt")
    torch.save({"Policy": actor.state_dict()}, run / "Employee-1500.pt")
    eb.main([str(tmp_path / "run"), "--as", "EmployeeBrain", "--out", str(tmp_path / "out")])
    assert (tmp_path / "out" / "EmployeeBrain.g.cs").exists()
    part = json.loads((tmp_path / "out" / "EmployeeBrain.part.json").read_text())
    assert part["card"]["steps"] == 1500


def test_forward_clamps_and_normalises():
    net = {"mean": np.zeros(2), "std": np.ones(2), "hidden": [(np.eye(2), np.zeros(2))], "heads": [(np.eye(2), np.zeros(2))]}
    out = eb.forward(net, np.array([[100.0, -100.0]]))[0][0]
    assert out[0] == pytest.approx(5 / (1 + np.exp(-5)))      # clamped to 5, then swish


def recurrent_actor():
    from mlagents_envs.base_env import ActionSpec, ObservationSpec, DimensionProperty, ObservationType
    from mlagents.trainers.torch_entities.networks import SimpleActor
    from mlagents.trainers.settings import NetworkSettings
    torch.manual_seed(19)
    spec = [ObservationSpec(shape=(6,), dimension_property=(DimensionProperty.NONE,),
                            observation_type=ObservationType.DEFAULT, name="obs")]
    settings = NetworkSettings(normalize=False, hidden_units=8, num_layers=2,
                               memory=NetworkSettings.MemorySettings(sequence_length=8, memory_size=8))
    return SimpleActor(spec, settings, ActionSpec(0, (4, 3)))


def test_recurrent_export_matches_actual_actor_over_a_sequence_and_reset(tmp_path):
    actor = recurrent_actor()
    result = eb.export(actor.state_dict(), "RecurrentProbe", {"inputs_version": 2}, str(tmp_path))
    assert result["parity"] < 1e-5
    net = result["net"]
    memory = np.zeros((2, 8), np.float32)
    trainer_memory = torch.zeros(1, 2, 8)
    random = np.random.default_rng(72)
    with torch.no_grad():
        for step in range(12):
            if step == 7:
                memory[:] = 0
                trainer_memory[:] = 0
            x = random.uniform(-1, 1, (2, 6)).astype(np.float32)
            ours = eb.forward(net, x, memory)
            enc, trainer_memory = actor.network_body([torch.as_tensor(x)], memories=trainer_memory)
            for a, branch in zip(ours, actor.action_model._discrete_distribution.branches):
                np.testing.assert_allclose(a, branch(enc).numpy(), atol=1e-6)
            np.testing.assert_allclose(memory, trainer_memory[0].numpy(), atol=1e-6)
    with pytest.raises(ValueError, match="per-bot"):
        eb.forward(net, x)


def test_recurrent_generated_csharp_runs_and_does_not_share_memory(tmp_path):
    import shutil
    import subprocess
    from pathlib import Path
    if not shutil.which("dotnet"):
        pytest.skip("offline .NET SDK unavailable")
    actor = recurrent_actor()
    net = eb.export(actor.state_dict(), "RecurrentProbe", {"inputs_version": 2}, str(tmp_path))["net"]
    x = np.array([[.1, -.2, .3, -.4, .5, -.6]], np.float32)
    memory = np.zeros((1, 8), np.float32)
    expected = [eb.forward(net, x, memory)[0][0].tolist() for _ in range(3)]
    root = Path(__file__).resolve().parents[3]
    stub = tmp_path / "Support.cs"
    stub.write_text('''
namespace UnityEngine {
 public enum RuntimeInitializeLoadType { SubsystemRegistration }
 public class RuntimeInitializeOnLoadMethodAttribute : System.Attribute {
  public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) {} }
}
namespace Kehai.Karen {
 public class ModelPack { public static ModelPack Current; public BrainNet Brain(string name) { return null; } }
}
class Probe {
 static void Assert(bool ok) { if (!ok) throw new System.Exception("state isolation failed"); }
 static void Main() {
  var n = Kehai.Karen.Generated.RecurrentProbe.Build();
  var a = new float[n.MemorySize]; var b = new float[n.MemorySize];
  var x = new float[] {.1f,-.2f,.3f,-.4f,.5f,-.6f};
  var first = n.Logits(x, a)[0];
  System.Console.WriteLine(string.Join(",", first));
  System.Console.WriteLine(string.Join(",", n.Logits(x,a)[0]));
  System.Console.WriteLine(string.Join(",", n.Logits(x,a)[0]));
  var isolated = n.Logits(x,b)[0];
  for(int i=0;i<first.Length;i++) Assert(first[i] == isolated[i]);
  System.Array.Clear(a,0,a.Length);
  var reset = n.Logits(x,a)[0];
  for(int i=0;i<first.Length;i++) Assert(first[i] == reset[i]);
  bool rejected=false; try { n.Logits(x); } catch(System.ArgumentException) { rejected=true; }
  Assert(rejected);
 }
}
''')
    import sys
    sys.path.insert(0, str(root / "tools/rl"))
    import offline_csharp
    output = offline_csharp.run([stub, tmp_path / "RecurrentProbe.g.cs", root / "Assets/!_Project/_Core/Scripts/Runtime/GameNames.cs",
        root / "Assets/!_Project/_Game/AI/Scripts/Decision/Policy/BrainNet.cs"], tmp_path)
    actual = [[float(x) for x in line.split(",")] for line in output.splitlines()]
    np.testing.assert_allclose(actual, expected, atol=1e-6)
