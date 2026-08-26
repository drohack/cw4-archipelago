using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Shows Archipelago server messages (item sends/receives, chat, hints) as
/// toasts that fade at the top of the screen DURING A MISSION, without pausing.
/// Not shown on the menu / level select (per design). Fed by ApClient's
/// MessageLog subscription; rendered every LateUpdate in the Game scene.
/// </summary>
public sealed class MessageToasts
{
    private const float Lifetime = 6.5f;   // seconds a toast stays up
    private const float FadeTime = 2.0f;   // seconds of fade at the end
    private const int MaxVisible = 6;

    private readonly Queue<string> _incoming = new();
    private readonly List<Toast> _active = new();
    private GameObject? _container;
    private TMP_FontAsset? _font;

    private sealed class Toast
    {
        public GameObject Go = null!;
        public TextMeshProUGUI Text = null!;
        public float Age;
    }

    /// <summary>Called from any thread's dispatch - queue for the main thread.</summary>
    public void Enqueue(string message)
    {
        lock (_incoming)
            _incoming.Enqueue(message);
    }

    public void LateTick(string scene)
    {
        if (scene != "Game")
        {
            if (_container != null) Teardown();
            return;
        }

        EnsureContainer();
        if (_container == null)
            return;

        // Spawn queued messages.
        lock (_incoming)
        {
            while (_incoming.Count > 0)
                Spawn(_incoming.Dequeue());
        }

        // Age, fade, and reap.
        float dt = Time.deltaTime;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var t = _active[i];
            t.Age += dt;
            if (t.Age >= Lifetime)
            {
                try { UnityEngine.Object.Destroy(t.Go); } catch { }
                _active.RemoveAt(i);
                continue;
            }
            float a = t.Age > Lifetime - FadeTime ? Mathf.Clamp01((Lifetime - t.Age) / FadeTime) : 1f;
            try
            {
                var c = t.Text.color; c.a = a; t.Text.color = c;
            }
            catch { }
        }
        Layout();
    }

    private void Spawn(string message)
    {
        if (_container == null) return;
        while (_active.Count >= MaxVisible)
        {
            try { UnityEngine.Object.Destroy(_active[0].Go); } catch { }
            _active.RemoveAt(0);
        }
        var go = new GameObject("ApToast");
        go.transform.SetParent(_container.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(900f, 26f);
        var txt = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) txt.font = _font;
        txt.text = message;
        txt.fontSize = 18f;
        txt.color = new Color(0.85f, 0.95f, 1f, 1f);
        txt.alignment = TextAlignmentOptions.Center;
        txt.enableWordWrapping = false;
        txt.overflowMode = TextOverflowModes.Ellipsis;
        _active.Add(new Toast { Go = go, Text = txt, Age = 0f });
    }

    private void Layout()
    {
        // Newest at top, stack downward.
        for (int i = 0; i < _active.Count; i++)
        {
            var rt = _active[_active.Count - 1 - i].Go.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition = new Vector2(0f, -30f - i * 26f);
        }
    }

    private void EnsureContainer()
    {
        if (_container != null)
        {
            try { if (_container.transform != null) return; } catch { }
            _container = null;   // destroyed on scene change
            _active.Clear();
        }

        Canvas host = null!;
        foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
            if (cv != null && cv.isRootCanvas) { host = cv; break; }
        if (host == null)
            return;

        if (_font == null)
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) { _font = f; break; }

        _container = new GameObject("ApToastContainer");
        _container.transform.SetParent(host.transform, false);
        _container.transform.SetAsLastSibling();
        var rt = _container.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private void Teardown()
    {
        foreach (var t in _active)
        {
            try { UnityEngine.Object.Destroy(t.Go); } catch { }
        }
        _active.Clear();
        try { if (_container != null) UnityEngine.Object.Destroy(_container); } catch { }
        _container = null;
    }
}
