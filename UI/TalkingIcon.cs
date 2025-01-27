using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace NuclearVOIP.UI
{
    internal class TalkingIcon: MonoBehaviour
    {
        private Text? text;

        private void Awake()
        {
            text = gameObject.AddComponent<Text>();
            text.font = Resources.FindObjectsOfTypeAll<Font>()
                .Where(a => a.name == "regular_cozy")
                .First();

            ContentSizeFitter fitter = gameObject.AddComponent<ContentSizeFitter>();
        }

        internal void Setup(Player player)
        {
            text!.text = player.GetNameOrCensored();
        }
    }
}
