using UnityEngine;
using UnityEngine.UI;

public class HandInputPanel : MonoBehaviour
{
    [SerializeField] HandAnimatorInput m_LeftHand;
    [SerializeField] HandAnimatorInput m_RightHand;
    [SerializeField] Image[] m_Bars = new Image[4];
    [SerializeField] Text[] m_Values = new Text[4];

    public void Configure(HandAnimatorInput left, HandAnimatorInput right, Image[] bars, Text[] values)
    {
        m_LeftHand = left;
        m_RightHand = right;
        m_Bars = bars;
        m_Values = values;
    }

    void Update()
    {
        Show(0, m_LeftHand != null ? m_LeftHand.Grip : 0f);
        Show(1, m_LeftHand != null ? m_LeftHand.Trigger : 0f);
        Show(2, m_RightHand != null ? m_RightHand.Grip : 0f);
        Show(3, m_RightHand != null ? m_RightHand.Trigger : 0f);
    }

    void Show(int index, float value)
    {
        m_Bars[index].fillAmount = value;
        m_Bars[index].color = Color.Lerp(new Color(0.3f, 0.75f, 1f), new Color(1f, 0.55f, 0.2f), value);
        m_Values[index].text = Mathf.RoundToInt(value * 100f) + "%";
    }
}
