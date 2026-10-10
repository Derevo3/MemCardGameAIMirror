using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public enum GamePhase { Waiting, SelectCard, PickWinner, BetweenRounds, GameOver }

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Данные")]
    public Card cardPrefab;
    public float cardSpacing = 1.2f;

    [Header("Якоря стола")]
    public Transform handSlotA, handSlotB;     // руки игроков
    public Transform situationSlot;            // центр стола
    public Transform situationCorner;          // угол экрана для situation-карты
    public Transform tableSlot;                // выбранные карты на столе
    public Transform foldSlot;                 // колода Fold

    [SyncVar(hook = nameof(OnPhaseChanged))] public GamePhase phase = GamePhase.Waiting;
    [SyncVar(hook = nameof(OnStatusChanged))] public string status = "Ожидание игроков";
    [SyncVar(hook = nameof(OnResultChanged))] public string result = "";

     // --- Новые поля для автосбора ---
    [Header("Автосбор спрайтов")]
    public string memPath = "MemCards";      // папка Assets/Resources/MemCards
    public string situationPath = "SituationCards"; // папка Assets/Resources/SituationCards

    private List<Sprite> memSprites = new List<Sprite>();
    private List<Sprite> situationSprites = new List<Sprite>();

    // Серверные структуры
    readonly List<int> memDeck = new List<int>();
    readonly List<int> situationDeck = new List<int>();
    readonly List<NetPlayer> players = new List<NetPlayer>();
    readonly Dictionary<NetPlayer, List<Card>> hands = new Dictionary<NetPlayer, List<Card>>();
    readonly Dictionary<NetPlayer, uint> chosenHandCard = new Dictionary<NetPlayer, uint>();
    readonly Dictionary<NetPlayer, uint> pickedTableCard = new Dictionary<NetPlayer, uint>();
    readonly List<Card> tableCards = new List<Card>();
    
    readonly List<Card> allCards = new List<Card>();
    Card situationCard;
    int foldCount;
    bool gameStarted;
    public Sprite memBackSprite;
    public Sprite situationBackSprite;

    void Awake()
    {
        InitializeCardLists();

        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Проверка рубашки: если в инспекторе пусто — сразу лог
        if (memBackSprite == null)
            Debug.LogError("[GameManager] Поле 'Mem Back Sprite' не назначено в инспекторе! Карты будут без рубашки.");
        else
            Debug.Log("[GameManager] Рубашка успешно назначена.");
    }

    public override void OnStartClient()
    {
        // UI: подписаться на status/result/phase при необходимости
    }

    // ---------- клиентские хуки для UI ----------
    void OnPhaseChanged(GamePhase o, GamePhase n) { /* UI */ }
    void OnStatusChanged(string o, string n) { /* UI: показать n */ }
    void OnResultChanged(string o, string n) { /* UI: показать итог */ }

    // Можно ли кликать по карте (вызывается клиентом)
    public bool IsCardClickable(Card c)
    {
        if (phase == GamePhase.SelectCard)
            return !c.faceUp && c.ownerNetId == (NetPlayer.Local ? NetPlayer.Local.netId : 0);
        if (phase == GamePhase.PickWinner)
            return c.faceUp;
        return false;
    }

    // ---------- СЕРВЕР ----------

    public void ServerRegisterPlayer(NetPlayer p)
    {
        if (gameStarted || players.Contains(p)) return;
        p.slot = players.Count;
        players.Add(p);
        hands[p] = new List<Card>();

        if (players.Count == 2)
            StartCoroutine(ServerRunGame());
    }

    void InitializeCardLists()
    {
        // Автоматически загружаем все спрайты из папок
        Sprite[] memRaw = Resources.LoadAll<Sprite>(memPath);
        Sprite[] situationRaw = Resources.LoadAll<Sprite>(situationPath);

        memSprites.Clear();
        situationSprites.Clear();

        memSprites.AddRange(memRaw);
        situationSprites.AddRange(situationRaw);

        Debug.Log($"[GameManager] Загружено: {memSprites.Count} карт Mem, {situationSprites.Count} Situation");

        if (memSprites.Count == 0) Debug.LogError("[GameManager] Нет карт в Resources/" + memPath);
        if (situationSprites.Count == 0) Debug.LogError("[GameManager] Нет карт в Resources/" + situationPath);
    }

     // Вспомогательный метод: получить спрайт по индексу (для клиента)
    public Sprite GetMemSprite(int index)
    {
        if (index < 0 || index >= memSprites.Count) return null;
        return memSprites[index];
    }

    public Sprite GetSituationSprite(int index)
    {
        if (index < 0 || index >= situationSprites.Count) return null;
        return situationSprites[index];
    }

    void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    IEnumerator ServerRunGame()
    {
        gameStarted = true;

        memDeck.Clear();
        situationDeck.Clear();

        int memCount = memSprites.Count;
        int situationCount = situationSprites.Count;

        for (int i = 0; i < memCount; i++) memDeck.Add(i);
        for (int i = 0; i < situationCount; i++) situationDeck.Add(i);

        Shuffle(memDeck);
        Shuffle(situationDeck);

        status = "Игра началась";
        yield return new WaitForSeconds(0.5f);

        // 1. Раздаём по 3 карты Mem
        for (int i = 0; i < 3; i++)
            foreach (var p in players)
                DealMemCard(p);

        RelayoutHands();

        // 9. Повторяем шаги 2–9, пока не кончатся карты
        while (situationDeck.Count > 0 && players.TrueForAll(p => hands[p].Count > 0))
        {
            int cardIndex = situationDeck[0];

            // Защита от выхода за границы
            if (cardIndex < 0 || cardIndex >= situationSprites.Count)
            {
                Debug.LogError($"[GameManager] Индекс {cardIndex} не найден в списке Situation (всего: {situationSprites.Count})");
                situationDeck.RemoveAt(0); // убираем проблемный индекс, чтобы не зациклиться
                continue;
            }

            Sprite sprite = GetSituationSprite(cardIndex);
            if (sprite == null)
            {
                Debug.LogError("[GameManager] Спрайт для Situation равен null");
                situationDeck.RemoveAt(0);
                continue;
            }

            // Теперь передаём 5 аргументов, включая sprite
            situationCard = SpawnCard(CardDeck.Situation, cardIndex, 0, situationSlot.position, sprite, memBackSprite);
            
            situationDeck.RemoveAt(0);

            // 3. Игроки выбирают карту из руки
            chosenHandCard.Clear();
            phase = GamePhase.SelectCard;
            status = "Выберите карту из руки";
            yield return new WaitUntil(() => chosenHandCard.Count == players.Count);

            // 4. Situation — в угол, выбранные карты — на стол лицом вверх
            // Выкладываем выбранные карты на стол и раскрываем их
            foreach (var kv in chosenHandCard)
            {
                Card c = GetCard(kv.Value);

                // Позиция на столе (с разлётом влево/вправо от tableSlot)
                int i0 = kv.Key.slot; // 0 или 1
                Vector3 basePos = tableSlot.position;
                Vector3 targetPos = i0 == 0
                    ? basePos - Vector3.right * (cardSpacing * 0.5f)
                    : basePos + Vector3.right * (cardSpacing * 0.5f);

                RpcMoveCard(c.netId, targetPos);

                // Раскрываем карту для всех
                c.faceUp = true;
                
                c.ownerNetId = 0; // <-- делаем общей, чтобы все видели лицом

                // Сбрасываем подсветку выбора — теперь цвет задаётся через faceUp (серый)
                c.selectedBy = 0; 

                hands[kv.Key].Remove(c);
                tableCards.Add(c);
            }
            
            RelayoutHands();

            // Ситуация уезжает в угол
            RpcMoveCard(situationCard.netId, situationCorner.position);

            phase = GamePhase.PickWinner;
            status = "Выберите карту на столе";


            // 5. Игроки выбирают карту на столе
            pickedTableCard.Clear();
            phase = GamePhase.PickWinner;
            status = "Выберите карту на столе";
            yield return new WaitUntil(() => pickedTableCard.Count == players.Count);

            // раскрытие: теперь оба видят, кто что выбрал
            foreach (var kv in pickedTableCard)
            {
                GetCard(kv.Value).selectedBy = 0; // Убираем подсветку, оставляем серый цвет
            }

            // 6. Очко тому, чью карту выбрали
            foreach (var kv in pickedTableCard)
            {
                foreach (var owner in players)
                    if (chosenHandCard[owner] == kv.Value)
                    {
                        owner.score++; // SyncVar сам разойдётся клиентам
                        break;
                    }
            }

            // 7. Все три карты — в Fold рубашкой вверх
            phase = GamePhase.BetweenRounds;
            RpcMoveCard(situationCard.netId, foldSlot.position + Vector3.right * (foldCount++ * 0.02f));
            situationCard.faceUp = false;
            situationCard.ownerNetId = 0;
            situationCard.selectedBy = 0;
            situationCard.inFold = true;

            foreach (var p in players)
            {
                Card c = GetCard(chosenHandCard[p]);
                RpcMoveCard(c.netId, foldSlot.position + Vector3.right * (foldCount++ * 0.02f));
                c.faceUp = false;
                c.inFold = true;
                c.ownerNetId = 0;
                c.selectedBy = 0;
            }
            situationCard = null;

            // 8. По одной карте каждому
            bool canContinue = true;
            foreach (var p in players)
                canContinue &= DealMemCard(p);

            RelayoutHands();

            yield return new WaitForSeconds(0.5f);
            if (!canContinue) break;
        }

        // финал: все карты рук — в Fold рубашкой вверх
        phase = GamePhase.BetweenRounds;
        foreach (var p in players)
        {
            foreach (var c in hands[p])
            {
                RpcMoveCard(c.netId, foldSlot.position + Vector3.right * (foldCount++ * 0.02f));
                c.faceUp = false;
                c.ownerNetId = 0;
                c.inFold = true;
            }
            hands[p].Clear();
        }

        // 10. Победитель
        phase = GamePhase.GameOver;
        int s0 = players[0].score, s1 = players[1].score;
        result = s0 == s1 ? "Ничья!"
               : $"Победил игрок {players[s0 > s1 ? 0 : 1].slot + 1} ({Mathf.Max(s0, s1)} : {Mathf.Min(s0, s1)})";
    }

    bool DealMemCard(NetPlayer p)
    {
        if (memDeck.Count == 0) return false;

        int cardIndex = memDeck[0];

        if (cardIndex < 0 || cardIndex >= memSprites.Count)
        {
            Debug.LogError($"[DealMemCard] Индекс карты {cardIndex} не найден в списке спрайтов. Всего карт: {memSprites.Count}");
            return false;
        }

        Transform anchor = p.slot == 0 ? handSlotA : handSlotB;
        Vector3 pos = anchor.position + Vector3.right * (hands[p].Count * cardSpacing);

        // Получаем спрайт по индексу
        Sprite cardSprite = GetMemSprite(cardIndex);
        if (cardSprite == null)
        {
            Debug.LogError($"[DealMemCard] Спрайт для индекса {cardIndex} равен null");
            return false;
        }

        // Передаём спрайт в SpawnCard (нужно будет чуть доработать сам SpawnCard — см. ниже)
        Card c = SpawnCard(CardDeck.Mem, cardIndex, p.netId, pos, cardSprite, memBackSprite);

        memDeck.RemoveAt(0);
        hands[p].Add(c);
        return true;
    }

    Card SpawnCard(CardDeck deck, int index, uint owner, Vector3 pos, Sprite sprite, Sprite memBackSprite)
    {
        Card c = Instantiate(cardPrefab, pos, Quaternion.identity);
        c.inFold = false;
        c.deck = deck;
        c.cardIndex = index;
        c.faceUp = false;
        c.ownerNetId = owner;
        c.SetSpriteImmediate(sprite);
        c.SetBackSpriteImmediate(memBackSprite);
        NetworkServer.Spawn(c.gameObject);
        allCards.Add(c);
        return c;
    }

    Card GetCard(uint netId)
    {
        if (NetworkServer.active && NetworkServer.spawned.TryGetValue(netId, out var serverNi))
            return serverNi.GetComponent<Card>();
        if (NetworkClient.active && NetworkClient.spawned.TryGetValue(netId, out var clientNi))
            return clientNi.GetComponent<Card>();
        return null;
    }
    // ---------- Серверная обработка кликов ----------

    public void ServerSelectCard(NetPlayer p, uint cardNetId)
    {
        if (phase != GamePhase.SelectCard || chosenHandCard.ContainsKey(p)) return;
        Card c = GetCard(cardNetId);
        if (c == null || c.ownerNetId != p.netId || !hands[p].Contains(c)) return;

        chosenHandCard[p] = cardNetId;
        c.selectedBy = p.netId;
    }

    public void ServerPickTableCard(NetPlayer p, uint cardNetId)
    {
        if (phase != GamePhase.PickWinner || pickedTableCard.ContainsKey(p)) return;
        Card c = GetCard(cardNetId);
        if (c == null || !tableCards.Contains(c)) return;

        pickedTableCard[p] = cardNetId;
        c.selectedBy = p.netId; // Подсветка видна только владельцу
    }

    [ClientRpc]
    void RpcMoveCard(uint cardNetId, Vector3 pos)
    {
        if (NetworkClient.spawned.TryGetValue(cardNetId, out var ni))
            {
                var card = ni.GetComponent<Card>();
                if (card != null)
                    card.SetTargetPosition(pos); // клиент сам плавно доедет до точки
            }
    }

    public void ServerRestart()
    {
        if (!gameStarted || phase != GamePhase.GameOver) return;

        StopAllCoroutines();

        // сносим все карты на свете
        foreach (var c in allCards)
            if (c != null) NetworkServer.Destroy(c.gameObject);
        allCards.Clear();

        hands.Clear();
        tableCards.Clear();
        situationCard = null;
        chosenHandCard.Clear();
        pickedTableCard.Clear();
        foldCount = 0;

        result = "";
        status = "Игра началась";

        foreach (var p in players)
        {
            p.score = 0;
            hands[p] = new List<Card>();
        }

        gameStarted = true;
        StartCoroutine(ServerRunGame());
    }

    void RelayoutHands()
    {
        for (int pi = 0; pi < players.Count; pi++)
        {
            Transform anchor = pi == 0 ? handSlotA : handSlotB;
            var hand = hands[players[pi]];
            for (int i = 0; i < hand.Count; i++)
                RpcMoveCard(hand[i].netId, anchor.position + Vector3.right * (i * cardSpacing));
        }
    }
}
