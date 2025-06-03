using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NuclearVOIP.UI
{
    internal class TalkingList: MonoBehaviour
    {
        private readonly Dictionary<Player, GameObject> entries = [];

        internal void AddPlayer(Player player)
        {
            GameObject go = new("TalkingIcon");
            TalkingIcon icon = go.AddComponent<TalkingIcon>();
            icon.Setup(player);

            go.transform.SetParent(gameObject.transform, false);

            entries.Add(player, go);
        }

        internal void RemovePlayer(Player player)
        {
            if (!entries.TryGetValue(player, out GameObject go))
                return;

            Destroy(go);
            entries.Remove(player);
        }

        private void Awake()
        {
            gameObject.AddComponent<VerticalLayoutGroup>();

            RectTransform rtrans = gameObject.GetComponent<RectTransform>();
            rtrans.anchorMax = new Vector2(1, 0);
            rtrans.anchorMin = new Vector2(0, 0);
            rtrans.pivot = new Vector2(0, 1);
        }
    }
}
