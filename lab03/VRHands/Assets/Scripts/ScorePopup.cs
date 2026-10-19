using UnityEngine;
using UnityEngine.UI;

public class ScorePopup : MonoBehaviour
{
    public float lifetime = 1.5f;
    public float riseSpeed = 0.35f;

    Text m_Text;
    Color m_Color;
    float m_Age;

    public static void Spawn(Vector3 position, string message, Color color, Font font)
    {
        var go = new GameObject("Score Popup");
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.0025f;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(400, 200);

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var rect = textGo.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        var text = textGo.AddComponent<Text>();
        text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 90;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = message;
        text.color = color;
        textGo.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);

        var popup = go.AddComponent<ScorePopup>();
        popup.m_Text = text;
        popup.m_Color = color;
    }

    void Update()
    {
        m_Age += Time.deltaTime;
        var t = m_Age / lifetime;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        transform.position += Vector3.up * (riseSpeed * Time.deltaTime);

        var cam = Camera.main;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);

        var scale = Mathf.Lerp(0.0015f, 0.0025f, Mathf.Clamp01(t * 6f));
        transform.localScale = Vector3.one * scale;
        var c = m_Color;
        c.a = 1f - Mathf.Clamp01((t - 0.5f) * 2f);
        m_Text.color = c;
    }
}
