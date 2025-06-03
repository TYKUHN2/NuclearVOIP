using BepInEx.Unity.Mono.Configuration;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace NuclearVOIP.UI
{
    internal class ChannelUI: MonoBehaviour
    {
        private Text text;

        private void Awake()
        {
            text = gameObject.AddComponent<Text>();
            text.font = Resources.FindObjectsOfTypeAll<Font>()
                .Where(a => a.name == "regular_cozy")
                .First();

            gameObject.AddComponent<ContentSizeFitter>();
        }

        private void Start()
        {
            text.text = $"Channel {Plugin.Instance.streamer!.channel + 1}";
        }

        private void Update()
        {
            OpusMultiStreamer streamer = Plugin.Instance.streamer!;
            KeyboardShortcut shortcut = Plugin.Instance.configChannelKey.Value;
            if (shortcut.IsDown())
            {
                streamer.channel = (byte)((streamer.channel + 1) % 10);
                text.text = $"Channel {streamer.channel + 1}";
            }
        }
    }
}
