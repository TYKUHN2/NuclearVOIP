using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Configuration;
using System;
using System.Threading;
using NuclearOption.SavedMission;
using System.IO;
using BepInEx.Logging;
using UnityEngine;
using System.Linq;

namespace NuclearVOIP
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInProcess("NuclearOption.exe")]
    public class Plugin : BaseUnityPlugin
    {
        private static Plugin? _Instance;
        internal static Plugin? Instance
        {
            get { return _Instance; }
        }

        internal new ManualLogSource Logger
        {
            get { return base.Logger; }
        }

        internal readonly ConfigEntry<KeyboardShortcut> configTalkKey;

        internal MicrophoneListener? activeListener;
        internal OpusEncoder? encoder;
        internal FileStream? fStream;
        internal OggOpus? oStream;
        internal int seq;

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
            Logger.LogDebug($"Debug logs enabled");

            MissionManager.onMissionStart += MissionHook;
            MissionManager.onMissionUnload += MissionUnload;
        }

        private void OnDestroy()
        {
            Logger.LogInfo($"Unloaded {MyPluginInfo.PLUGIN_GUID}");
        }

        private void MissionHook(Mission mission)
        {
            activeListener = GameManager.LocalPlayer.gameObject.AddComponent<MicrophoneListener>();
            encoder = new(activeListener.stream);
            fStream = new("test.opus", FileMode.OpenOrCreate | FileMode.Truncate);
            fStream.SetLength(0);

            Ogg oFile = new();
            oStream = new(oFile, 1, (short)encoder.lookahead, 20);

            encoder.OnData += (stream) =>
            {
                byte[][]? frames = encoder.Read(encoder.Count());
                if (frames == null)
                    return;

                oStream.Write(frames);
            };

            oStream.OnData += (stream) =>
            {
                byte[][]? pages = oStream.Read(oStream.Count());
                if (pages == null)
                    return;

                fStream.Write(pages.SelectMany(a => a).ToArray());
            };
        }

        private void MissionUnload()
        {
            activeListener = null;
            encoder = null;

            oStream!.Close();
            fStream!.Close();
        }

        private void Update()
        {
            if (activeListener == null)
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
    }
}
