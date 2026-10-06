using Mirror;
using UnityEngine;

public class Card : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnAnyChanged))] public CardDeck deck;
    [SyncVar(hook = nameof(OnAnyChanged))] public int cardIndex;
    [SyncVar(hook = nameof(OnAnyChanged))] public bool faceUp;
    [SyncVar(hook = nameof(OnAnyChanged))] public uint ownerNetId;
    [SyncVar(hook = nameof(OnAnyChanged))] public uint selectedBy; // кто выбрал эту карту
    [SyncVar(hook = nameof(OnAnyChanged))] public bool inFold;

    public CardDatabase db;
    SpriteRenderer rend;

    void Awake() => rend = GetComponent<SpriteRenderer>();

    public override void OnStartClient() => RefreshVisual();

    // Хуки для всех SyncVar
    void OnAnyChanged(uint oldV, uint newV) => RefreshVisual();
    void OnAnyChanged(CardDeck o, CardDeck n) => RefreshVisual();
    void OnAnyChanged(int o, int n) => RefreshVisual();
    void OnAnyChanged(bool o, bool n) => RefreshVisual();

    void RefreshVisual()
    {
        if (rend == null) rend = GetComponent<SpriteRenderer>();
    
        uint myNetId = NetPlayer.Local != null ? NetPlayer.Local.netId : 0;
        bool isMyCard = ownerNetId == myNetId;
        bool isChosenByMe = selectedBy == myNetId;
    
        bool showFront = faceUp || isMyCard;
        rend.sprite = showFront
            ? db.GetFront(deck, cardIndex)
            : (inFold ? db.foldBack : db.GetBack(deck));
    
        if (inFold)
        {
            rend.color = Color.white;
            return;
        }
    
        rend.color = isChosenByMe ? new Color(0.85f, 0.85f, 0.5f) : Color.white;
    }


    void OnMouseUpAsButton()
    {
        Debug.Log($"Клик по карте {name}. Local={NetPlayer.Local != null}, Phase={GameManager.Instance?.phase}");
        if (NetPlayer.Local == null || GameManager.Instance == null) return;
        if (!GameManager.Instance.IsCardClickable(this)) return;

        if (GameManager.Instance.phase == GamePhase.SelectCard)
            NetPlayer.Local.CmdSelectCard(netId);
        else if (GameManager.Instance.phase == GamePhase.PickWinner)
            NetPlayer.Local.CmdPickTableCard(netId);
    }
}
