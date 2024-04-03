using UnityEngine;
using UnityEngine.UI;

namespace COSMP.Network.Core
{
    internal class PlayerCanvas : MonoBehaviour
    {
        internal Camera camera;
        internal Canvas canvas;
        internal Vector3 offset;
        internal Image action;

        internal void Init(string username)
        {
            camera = Camera.main;
            offset = new(0, 2, 0);

            // Canvas
            GameObject canvasGo = new("PlayerCanvas");
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.worldCamera = camera;
            canvas.transform.localScale = Vector3.one * 0.005f;

            // Image
            GameObject actionGo = new("Action");
            actionGo.transform.SetParent(canvasGo.transform, false);
            actionGo.transform.localPosition = new Vector3(0, 50f, 0);
            action = actionGo.AddComponent<Image>();
            action.enabled = false;

            // Text
            GameObject textGo = new("Username");
            textGo.transform.SetParent(canvasGo.transform, false);
            Text text = textGo.AddComponent<Text>();
            text.text = username;
            text.fontSize = 52;
            text.font = COSML.MainMenu.MenuResources.GetFontByName("Geomanist-Medium");
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        internal void Loop()
        {
            canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - camera.transform.position);
            canvas.transform.position = transform.position + offset;
        }

        internal void Toggle(bool display)
        {
            canvas.gameObject.SetActive(display);
            if (!display) SetAction(PlayerCanvasAction.None);
        }

        internal void SetAction(PlayerCanvasAction playerAction)
        {
            bool enable = COSMP.CanvasActions.ContainsKey(playerAction);
            action.sprite = enable ? COSMP.CanvasActions[playerAction] : null;
            action.enabled = enable;
        }
    }

    internal enum PlayerCanvasAction
    {
        None,
        Pause,
        Journal,
        Inventory,
    }
}
