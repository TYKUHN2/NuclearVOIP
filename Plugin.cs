using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Configuration;
using System;
using System.Threading;
using NuclearOption.SavedMission;
using System.IO;
using BepInEx.Logging;
using System.Linq;
using NuclearOption.Networking;

namespace NuclearVOIP
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInProcess("NuclearOption.exe")]
    public class Plugin : BaseUnityPlugin
    {
        private static Plugin? _Instance;
        internal static Plugin Instance
        {
            get
            {
                if (_Instance == null)
                    throw new InvalidOperationException("Plugin not initialized");

                return _Instance;
            }
        }

        internal new ManualLogSource Logger
        {
            get { return base.Logger; }
        }

        internal readonly ConfigEntry<KeyboardShortcut> configTalkKey;

        internal MicrophoneListener activeListener;
        internal FileStream? fStream;
        internal OggOpus? oStream;

        Plugin()
        {
            if (Interlocked.CompareExchange(ref _Instance, this, null) != null) // I like being thread safe okay?
                throw new InvalidOperationException($"Reinitialization of Plugin {MyPluginInfo.PLUGIN_GUID}");

            configTalkKey = Config.Bind(
                    "General",
                    "TalkKey",
                    new KeyboardShortcut(UnityEngine.KeyCode.V),
                    "Push to talk key"
                );
        }

        ~Plugin()
        {
            _Instance = null;
        }

        private void Awake()
        {
            Logger.LogInfo($"Loaded {MyPluginInfo.PLUGIN_GUID}");
            activeListener = gameObject.AddComponent<MicrophoneListener>();

            MissionManager.onMissionStart += MissionHook;
        }

        private void OnDestroy()
        {
            Logger.LogInfo($"Unloaded {MyPluginInfo.PLUGIN_GUID}");
        }

        private void MissionHook(Mission mission)
        {
            NetworkManagerNuclearOption.i.Client.Disconnected.AddListener(MissionUnload);

            fStream = new("test.opus", FileMode.OpenOrCreate);
            fStream.SetLength(0);
            fStream.Flush();

            oStream = new(new(), activeListener.stream, 1, 20);
            FlushOStream();

            oStream.OnData += (stream) =>
            {
                FlushOStream();
            };
        }

        private void MissionUnload(Mirage.ClientStoppedReason reason)
        {
            NetworkManagerNuclearOption.i.Client.Disconnected.RemoveListener(MissionUnload);

            if (fStream == null)
                return;

            oStream?.Close();
            fStream?.Close();

            activeListener.enabled = false;

            float[]? originalPCM = oStream!.encoder.original.Read(oStream!.encoder.original.Count());

            using FileStream raw = new("test.raw", FileMode.OpenOrCreate);
            raw.SetLength(0);
            raw.Flush();

            if (originalPCM != null)
            {
                byte[] buf = new byte[originalPCM!.Length << 2];
                Buffer.BlockCopy(originalPCM, 0, buf, 0, buf.Length);

                raw.Write(buf, 0, buf.Length);
            }

            raw.Close();

            float[] fPCM = originalPCM.Select(a => a * short.MaxValue).ToArray();

            short[] pcm = fPCM.Select(a => (short)a).ToArray();
            short[] reversed = Utils.ReverseEndianness(pcm);
            Array.Resize(ref reversed, ((reversed.Length / 960) + 1) * 960);

            byte[] bPCM = new byte[reversed.Length * 2];
            Buffer.BlockCopy(reversed, 0, bPCM, 0, bPCM.Length);

            oStream = null;
            fStream = null;
        }

        private void Update()
        {
            if (fStream == null)
                return;

            if (configTalkKey.Value.IsPressed())
                activeListener.enabled = true;
            else if (activeListener.enabled)
            {
                activeListener.enabled = false;
                oStream!.Flush();
                fStream!.Flush();
            }
        }

        private void FlushOStream()
        {
            byte[][]? pages = oStream!.Read(oStream.Count());
            if (pages == null)
                return;

            Logger.LogDebug($"Writing OggOpus {pages.Length} page(s) to file");
            fStream!.Write(pages.SelectMany(a => a).ToArray());
        }
    }
}
