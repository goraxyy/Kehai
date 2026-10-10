// The in-engine webcam path (IDEAS.md "Deployment: Export ONNX → Unity Sentis").
//
// Compiled only when the project defines KAREN_SENTIS, because it needs the Unity Inference
// Engine package (com.unity.ai.inference, formerly Sentis), which this project doesn't ship
// with. To enable it:
//   1. Package Manager → add com.unity.ai.inference.
//   2. Train the eye model with tools/blink/train_eye_cnn.py and drop eye_cnn.onnx into
//      Assets (it imports as a ModelAsset). The model is yours; it isn't in the repository.
//   3. Player Settings → Scripting Define Symbols → add KAREN_SENTIS.
//   4. Assign the ModelAsset on a SentisBlinkDriver in the scene, press F9 to calibrate.
//
// Until you have a face detector in-engine too, this runs the eye model on a fixed crop:
// calibration asks you to hold still, and the eye box is found once from the frame's
// darkest horizontal band. The sidecar path (UdpBlinkSource) is the full pipeline with
// MediaPipe detection and tracking; this one trades that for zero external processes.
//
// NOTE: written against the Inference Engine 2.x API but not compiled in this repository's
// CI, since the package isn't installed here.
#if KAREN_SENTIS
using Unity.InferenceEngine;
using UnityEngine;

namespace Kehai.Blink
{
    public sealed class SentisBlinkSource : IBlinkSource
    {
        const int Size = 24;                       // the CNN's input: 24×24 grayscale eye crop

        readonly WebCamTexture camera;
        readonly Worker worker;
        readonly Tensor<float> input;
        readonly Texture2D crop;
        RectInt eye;
        double lastFrame;
        float lastInferenceMs;

        public string Name => "webcam in-engine (Sentis CNN)";
        public bool IsLive => camera != null && camera.isPlaying && BlinkClock.Now - lastFrame < 1.0;
        public float MeasuredLatencyMs => lastInferenceMs + 1000f / Mathf.Max(1f, camera != null ? camera.requestedFPS : 30f);

        public SentisBlinkSource(ModelAsset modelAsset, string deviceName = null, int fps = 60)
        {
            camera = string.IsNullOrEmpty(deviceName) ? new WebCamTexture(640, 480, fps) : new WebCamTexture(deviceName, 640, 480, fps);
            camera.Play();
            Model model = ModelLoader.Load(modelAsset);
            worker = new Worker(model, BackendType.GPUCompute);
            input = new Tensor<float>(new TensorShape(1, 1, Size, Size));
            crop = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            eye = new RectInt(camera.width / 2 - 60, camera.height / 2 - 40, 120, 60);
        }

        // Place the eye box — called by calibration once the player is still.
        public void SetEyeBox(RectInt box) => eye = box;

        public bool TryRead(out BlinkSample sample)
        {
            sample = default;
            if (camera == null || !camera.didUpdateThisFrame) return false;
            double captured = BlinkClock.Now;
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // Grayscale, nearest-neighbour downsample of the eye box into the 24×24 input.
            // Rows go top-down, as train_eye_cnn.py saw them (a texture's origin is bottom-left).
            Color32[] frame = camera.GetPixels32();
            var data = new float[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int sx = eye.x + x * eye.width / Size;
                int sy = eye.y + eye.height - 1 - y * eye.height / Size;
                Color32 c = frame[Mathf.Clamp(sy, 0, camera.height - 1) * camera.width + Mathf.Clamp(sx, 0, camera.width - 1)];
                data[y * Size + x] = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
            }
            input.Upload(data);
            worker.Schedule(input);
            var output = worker.PeekOutput() as Tensor<float>;
            float[] result = output.DownloadToArray();
            float closed = result.Length > 1 ? Softmax(result)[1] : Sigmoid(result[0]);

            lastInferenceMs = (float)watch.Elapsed.TotalMilliseconds;
            lastFrame = captured;
            sample = new BlinkSample { Closed = closed, Confidence = 0.8f, Captured = captured, Source = Name };
            return true;
        }

        static float Sigmoid(float x) => 1f / (1f + Mathf.Exp(-x));

        static float[] Softmax(float[] v)
        {
            float max = Mathf.Max(v[0], v[1]);
            float a = Mathf.Exp(v[0] - max), b = Mathf.Exp(v[1] - max);
            return new[] { a / (a + b), b / (a + b) };
        }

        public void Dispose()
        {
            camera?.Stop();
            worker?.Dispose();
            input?.Dispose();
        }
    }

    // Scene component that wires the Sentis source into the tracker.
    public sealed class SentisBlinkDriver : MonoBehaviour
    {
        public ModelAsset eyeModel;
        public string deviceName;
        SentisBlinkSource source;

        void Start()
        {
            if (eyeModel == null || !BlinkTracker.Consented) return;
            source = new SentisBlinkSource(eyeModel, deviceName);
            BlinkTracker.Instance?.UseSource(source);
        }

        void OnDestroy() => source?.Dispose();
    }
}
#endif
