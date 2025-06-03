using UnityEngine;
using UnityEngine.UI;

namespace NuclearVOIP.UI
{
    internal class UIGroup: MonoBehaviour
    {
        private void Awake()
        {
            gameObject.AddComponent<VerticalLayoutGroup>();

            RectTransform rtrans = gameObject.GetComponent<RectTransform>();
            rtrans.anchorMax = new Vector2(1, 0);
            rtrans.anchorMin = new Vector2(0, 0);
            rtrans.pivot = new Vector2(0, 1);

            CanvasGroup group = gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            GameObject go = new("ChannelUI");
            go.transform.SetParent(transform, false);

            go.AddComponent<ChannelUI>();
        }
    }
}
