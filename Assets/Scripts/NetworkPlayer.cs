using UnityEngine;
using Mirror;

public class NetworkPlayer : NetworkBehaviour
{
    public static NetworkPlayer Local;

    [SyncVar] public int playerIndex = -1;

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Local = this;
    }

    // Клиент → Сервер: выбор карты (из руки или голосование)
    [Command]
    public void CmdSelectCard(uint cardNetId)
    {
        NetworkGameManager.Instance.HandleCardSelection(playerIndex, cardNetId);
    }

    // Сервер → Конкретный клиент: показать/скрыть лицо карты
    [TargetRpc]
    public void TargetSetCardFace(uint cardNetId, bool faceUp)
    {
        if (NetworkClient.spawned.TryGetValue(cardNetId, out var identity))
        {
            var card = identity.GetComponent<Card>();
            if (card != null) card.SetLocalFaceUp(faceUp);
        }
    }

    // Сервер → Конкретный клиент: включить клик по картам
    [TargetRpc]
    public void TargetEnableCards(uint[] cardNetIds)
    {
        foreach (uint id in cardNetIds)
        {
            if (NetworkClient.spawned.TryGetValue(id, out var identity))
            {
                var card = identity.GetComponent<Card>();
                if (card != null) card.IsClickable = true;
            }
        }
    }
}