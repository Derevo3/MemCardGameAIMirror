using System;
using UnityEngine;
using Mirror;

public class Card : NetworkBehaviour
{
    public enum CardType { Mem, Situation }

    [SyncVar] public CardType type;
    [SyncVar] public int cardId;
    [SyncVar] int frontSpriteIndex = -1;
    [SyncVar] public float moveSpeed = 50f;

    [SyncVar(hook = nameof(OnFaceUpChanged))]
    bool networkFaceUp;

    [SyncVar(hook = nameof(OnSortingOrderChanged))]
    int networkSortingOrder;

    [SyncVar(hook = nameof(OnTargetPosChanged))]
    Vector3 networkTargetPos;

    public bool IsClickable { get; set; }

    private SpriteRenderer sr;
    private Vector3 targetPos;
    private bool isMoving;
    private Action onArrive;
    private bool localFaceUpOverride;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    // Вызывается ТОЛЬКО на сервере до NetworkServer.Spawn
    public void Init(CardType type, int id, int spriteIndex)
    {
        this.type = type;
        cardId = id;
        frontSpriteIndex = spriteIndex;
        networkFaceUp = false;
        networkSortingOrder = 0;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        targetPos = transform.position;
        UpdateDisplay();
        UpdateCollider();
    }

    void OnFaceUpChanged(bool old, bool val) => UpdateDisplay();
    void OnSortingOrderChanged(int old, int val) { if (sr) sr.sortingOrder = val; }
    void OnTargetPosChanged(Vector3 old, Vector3 val)
    {
        targetPos = val;
        isMoving = true;
    }

    void UpdateDisplay()
    {
        if (sr == null) return;
        bool showFace = localFaceUpOverride || networkFaceUp;
        var gm = NetworkGameManager.Instance;
        if (gm == null) return;

        if (showFace && frontSpriteIndex >= 0)
        {
            var sprites = type == CardType.Situation
                ? gm.situationSprites
                : gm.memSprites;
            if (frontSpriteIndex < sprites.Length)
                sr.sprite = sprites[frontSpriteIndex];
        }
        else if (gm.backside != null)
        {
            sr.sprite = gm.backside;
        }
    }

    void UpdateCollider()
    {
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null && sr != null && sr.sprite != null)
            col.size = sr.sprite.bounds.size;
    }

    // Сервер: синхронит для всех
    public void SetFaceUp(bool faceUp) => networkFaceUp = faceUp;

    // Клиент: локальный показ (только владелец видит лицо)
    public void SetLocalFaceUp(bool faceUp)
    {
        localFaceUpOverride = faceUp;
        UpdateDisplay();
    }

    public void SetSortingOrder(int order) => networkSortingOrder = order;

    // Сервер: запускает движение, SyncVar синхронит цель всем
    public void MoveTo(Vector3 target, Action callback = null)
    {
        networkTargetPos = new Vector3(target.x, target.y, 0f);
        targetPos = networkTargetPos;
        isMoving = true;
        onArrive = callback;
    }

    void Update()
    {
        if (!isMoving) return;

        transform.position = Vector3.MoveTowards(
            transform.position, targetPos, moveSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPos) < 0.01f)
        {
            transform.position = targetPos;
            isMoving = false;
            if (isServer) // колбэк срабатывает только на сервере
            {
                onArrive?.Invoke();
                onArrive = null;
            }
        }
    }

    void OnMouseDown()
    {
        if (!IsClickable) return;
        if (NetworkPlayer.Local != null)
            NetworkPlayer.Local.CmdSelectCard(netIdentity.netId);
    }
}