using Mirror;
using UnityEngine;

public class Card : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnAnyChanged))] public CardDeck deck;
    [SyncVar(hook = nameof(OnAnyChanged))] public int cardIndex;
    [SyncVar(hook = nameof(OnAnyChanged))] public bool faceUp;
    [SyncVar(hook = nameof(OnAnyChanged))] public uint ownerNetId;
    [SyncVar(hook = nameof(OnAnyChanged))] public uint selectedBy;
    [SyncVar(hook = nameof(OnAnyChanged))] public bool inFold;

    [SerializeField] private Sprite backSprite;
    [SerializeField] float moveSpeed = 12f;
    Vector3 targetPosition;
    bool isMoving;  

    SpriteRenderer rend;
    void Awake() => rend = GetComponent<SpriteRenderer>();

    public override void OnStartClient()
    {
        targetPosition = transform.position;
    }

    public void SetTargetPosition(Vector3 pos)
    {
        targetPosition = pos;
        isMoving = true;
    }

    void Update()
    {
        if (!isMoving) return;

        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPosition) < 0.01f)
        {
            transform.position = targetPosition;
            isMoving = false;
        }
    }

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
            ? GameManager.Instance.GetMemSprite(cardIndex)
            : backSprite;

        /*Sprite faceSprite = null;
        if (GameManager.Instance != null)
        {
            faceSprite = GameManager.Instance.GetMemSprite(cardIndex);
        }

        if (faceSprite != null)
        {
            rend.sprite = faceSprite;
        }*/

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
