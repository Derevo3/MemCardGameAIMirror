using Mirror;
using UnityEngine;

public class Card : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnAnyChanged))] public CardDeck deck;
    [SyncVar(hook = nameof(OnAnyChanged))] public int cardIndex;
    [SyncVar(hook = nameof(OnAnyChanged))] public bool faceUp;
    [SyncVar(hook = nameof(OnAnyChanged))] public uint ownerNetId; // 0 = никому
    [SyncVar(hook = nameof(OnAnyChanged))] public uint selectedBy; // кто уже выбрал (для подсветки)

    [SyncVar(hook = nameof(OnAnyChanged))] public bool inFold;

    public CardDatabase db;

    SpriteRenderer rend;

    void Awake() => rend = GetComponent<SpriteRenderer>();

    public override void OnStartClient() => RefreshVisual();

    void OnAnyChanged(uint oldV, uint newV) => RefreshVisual();
    void OnAnyChanged(CardDeck o, CardDeck n) => RefreshVisual();
    void OnAnyChanged(int o, int n) => RefreshVisual();
    void OnAnyChanged(bool o, bool n) => RefreshVisual();

    void RefreshVisual()
    {
        if (rend == null) rend = GetComponent<SpriteRenderer>();
        bool showFront = faceUp || ownerNetId == (NetPlayer.Local ? NetPlayer.Local.netId : 0);

        // в Fold лежит рубашка foldBack, в руках и на столе — рубашка своей колоды
        bool inFold = this.inFold; // см. правку ниже
        rend.sprite = showFront ? db.GetFront(deck, cardIndex) : (inFold ? db.foldBack : db.GetBack(deck));

        rend.color = selectedBy != 0 ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
    }

    // Клик мышью по карте
    void OnMouseUpAsButton()
    {
        if (NetPlayer.Local == null || GameManager.Instance == null) return;
        if (!GameManager.Instance.IsCardClickable(this)) return;

        if (GameManager.Instance.phase == GamePhase.SelectCard)
            NetPlayer.Local.CmdSelectCard(netId);
        else
            NetPlayer.Local.CmdPickTableCard(netId);
    }
}
