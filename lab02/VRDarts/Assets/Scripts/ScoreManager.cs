using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps the game score and shows it on the world-space scoreboard.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    public Text totalText;
    public Text lastThrowText;
    public Text statsText;

    int m_Total;
    int m_Throws;
    int m_Hits;
    int m_Best;

    void Awake()
    {
        Instance = this;
        Refresh("Grab a dart and throw it at the board!");
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void RegisterHit(string label, int basePoints, float multiplier, float throwDistance, int total)
    {
        m_Throws++;
        if (basePoints > 0)
            m_Hits++;
        m_Total += total;
        m_Best = Mathf.Max(m_Best, total);

        Refresh($"{label}: {basePoints} x{multiplier:0.#} ({throwDistance:0.0} m) = <b>{total}</b>");
    }

    public void RegisterMiss()
    {
        m_Throws++;
        Refresh("Missed the board! 0 points");
    }

    public void ResetScore()
    {
        m_Total = m_Throws = m_Hits = m_Best = 0;
        foreach (var dart in FindObjectsByType<Dart>(FindObjectsSortMode.None))
            dart.ReturnToSpawn();
        Refresh("Score reset. Good luck!");
    }

    void Refresh(string lastThrow)
    {
        if (totalText != null)
            totalText.text = $"SCORE: {m_Total}";
        if (lastThrowText != null)
            lastThrowText.text = lastThrow;
        if (statsText != null)
            statsText.text = $"Darts thrown: {m_Throws}   Hits: {m_Hits}   Best throw: {m_Best}";
    }
}
