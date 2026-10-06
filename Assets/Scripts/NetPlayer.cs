using Mirror;
using UnityEngine;

public class NetPlayer : NetworkBehaviour
{
    public static NetPlayer Local;

    [SyncVar] public int slot = -1;
    [SyncVar(hook = nameof(OnScoreChanged))] public int score;

    public override void OnStartServer()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.ServerRegisterPlayer(this);
    }

    public override void OnStartLocalPlayer()
    {
        Local = this;
        // здесь можно вызвать UI: "вы игрок №" + slot
    }

    void OnScoreChanged(int oldV, int newV)
    {
        // UI: обновить счёт игрока slot == newV
    }

    [Command]
    public void CmdSelectCard(uint cardNetId) =>
        GameManager.Instance.ServerSelectCard(this, cardNetId);

    [Command]
    public void CmdPickTableCard(uint cardNetId) =>
        GameManager.Instance.ServerPickTableCard(this, cardNetId);
}
