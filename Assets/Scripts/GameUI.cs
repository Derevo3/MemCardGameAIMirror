using Mirror;
using UnityEngine;
using TMPro;

public class GameUI : MonoBehaviour
{
    public GameManager manager;
    public TMP_Text statusText;
    public TMP_Text scoreText;
    public TMP_Text resultText;
    public GameObject restartButton;

    void Start()
    {
        if (manager == null) manager = GameManager.Instance;
    }

    void Update()
    {
        if (manager == null) return;

        statusText.text = manager.status;
        resultText.text = manager.result;
        resultText.gameObject.SetActive(!string.IsNullOrEmpty(manager.result));
        restartButton.SetActive(manager.phase == GamePhase.GameOver);

        scoreText.text = "Игрок 1: " + ScoreOf(0) + "   Игрок 2: " + ScoreOf(1);
    }

    int ScoreOf(int slot)
    {
        foreach (var p in FindObjectsByType<NetPlayer>(FindObjectsSortMode.None))
            if (p.slot == slot) return p.score;
        return 0;
    }

    public void OnRestartClicked()
    {
        if (NetPlayer.Local != null)
            NetPlayer.Local.CmdRestart();
    }
}
