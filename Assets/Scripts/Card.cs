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

    // Рубашка задаётся ДО NetworkServer.Spawn — это критично
    [SerializeField] private Sprite _backSprite;

    [SerializeField] float moveSpeed = 12f;
    Vector3 targetPosition;
    bool isMoving;

    private SpriteRenderer _rend;

    public override void OnStartClient()
    {
        targetPosition = transform.position;
    }

    public void SetTargetPosition(Vector3 pos)
    {
        targetPosition = pos;
        isMoving = true;
    }
    public void SetSpriteImmediate(Sprite sprite)
    {
        if (_rend == null) _rend = GetComponent<SpriteRenderer>();
        if (sprite == null)
        {
            Debug.LogWarning($"[Card] Попытка поставить null-спрайт для карты {name}. Карта будет невидимой.");
            return;
        }
        _rend.sprite = sprite;
        // Важно: сразу ставим белый цвет, чтобы не было артефактов
        _rend.color = Color.white;
    }

    /// <summary>
    /// Ставит рубашку. Вызывается на сервере ДО NetworkServer.Spawn.
    /// </summary>
    public void SetBackSpriteImmediate(Sprite sprite)
    {
        _backSprite = sprite;
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

    // Хуки на все SyncVar — вызывают RefreshVisual при изменении любого из них
    void OnAnyChanged(uint o, uint n) => RefreshVisual();
    void OnAnyChanged(CardDeck o, CardDeck n) => RefreshVisual();
    void OnAnyChanged(int o, int n) => RefreshVisual();
    void OnAnyChanged(bool o, bool n) => RefreshVisual();

    void RefreshVisual()
    {
        if (_rend == null) return;
        
        uint myNetId = NetPlayer.Local?.netId ?? 0;
        bool isMyCard = (ownerNetId != 0 && ownerNetId == myNetId);
        bool isChosenByMe = selectedBy == myNetId;
        bool isSharedCard = (ownerNetId == 0);

        // Логика: показываем лицо, если карта открыта ИЛИ это моя карта
        bool showFront = faceUp || isMyCard || isSharedCard;

        // Сброс: всегда показываем рубашку (или белый спрайт)
        if (inFold)
        {
            if (_backSprite != null)
                _rend.sprite = _backSprite;
            _rend.color = Color.white;
            return;
        }

        // Если НЕ показываем лицо — показываем рубашку
        if (!showFront)
        {
            if (_backSprite != null)
            {
                _rend.sprite = _backSprite;
                _rend.color = Color.white;
            }
            else
            {
                _rend.color = Color.white; 
                Debug.LogWarning($"[Card] У карты {name} нет рубашки. Карта останется без обновления.");
            }
            return;
        }

        // Показываем лицо: спрайт уже поставлен через SetSpriteImmediate
        _rend.color = isChosenByMe ? new Color(0.85f, 0.85f, 0.5f) : Color.white;
    }

    void OnMouseUpAsButton()
    {
        if (NetPlayer.Local == null || GameManager.Instance == null) return;
        if (!GameManager.Instance.IsCardClickable(this)) return;

        if (GameManager.Instance.phase == GamePhase.SelectCard)
            NetPlayer.Local.CmdSelectCard(netId);
        else if (GameManager.Instance.phase == GamePhase.PickWinner)
            NetPlayer.Local.CmdPickTableCard(netId);
    }
}
