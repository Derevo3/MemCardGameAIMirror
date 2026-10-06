using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public enum GamePhase { Waiting, SelectCard, PickWinner, BetweenRounds, GameOver }

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Данные")]
    public CardDatabase db;
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

    // Серверные структуры
    readonly List<int> memDeck = new List<int>();
    readonly List<int> situationDeck = new List<int>();
    readonly List<NetPlayer> players = new List<NetPlayer>();
    readonly Dictionary<NetPlayer, List<Card>> hands = new Dictionary<NetPlayer, List<Card>>();
    readonly Dictionary<NetPlayer, uint> chosenHandCard = new Dictionary<NetPlayer, uint>();
    readonly Dictionary<NetPlayer, uint> pickedTableCard = new Dictionary<NetPlayer, uint>();
    readonly List<Card> tableCards = new List<Card>();
    Card situationCard;
    int foldCount;
    bool gameStarted;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
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
        for (int i = 0; i < db.GetDeckSize(CardDeck.Mem); i++) memDeck.Add(i);
        for (int i = 0; i < db.GetDeckSize(CardDeck.Situation); i++) situationDeck.Add(i);
        Shuffle(memDeck);
        Shuffle(situationDeck);

        status = "Игра началась";
        yield return new WaitForSeconds(0.5f);

        // 1. Раздаём по 3 карты Mem
        for (int i = 0; i < 3; i++)
            foreach (var p in players)
                DealMemCard(p);

        // 9. Повторяем шаги 2–9, пока не кончатся карты
        while (situationDeck.Count > 0 && players.TrueForAll(p => hands[p].Count > 0))
        {
            // 2. Situation в центр
            situationCard = SpawnCard(CardDeck.Situation, situationDeck[0], 0, situationSlot.position);
            situationDeck.RemoveAt(0);

            // 3. Игроки выбирают карту из руки
            chosenHandCard.Clear();
            phase = GamePhase.SelectCard;
            status = "Выберите карту из руки";
            yield return new WaitUntil(() => chosenHandCard.Count == players.Count);

            // 4. Situation — в угол, выбранные карты — на стол лицом вверх
            RpcMoveCard(situationCard.netId, situationCorner.position);
            int i0 = 0;
            tableCards.Clear();
            foreach (var p in players)
            {
                Card c = GetCard(chosenHandCard[p]);
                Vector3 basePos = tableSlot.position;
                RpcMoveCard(c.netId, i0 == 0
                    ? basePos - Vector3.right * (cardSpacing * 0.5f)
                    : basePos + Vector3.right * (cardSpacing * 0.5f));
                c.faceUp = true;
                c.selectedBy = 0;
                tableCards.Add(c);
                i0++;
            }

            // 5. Игроки выбирают карту на столе
            pickedTableCard.Clear();
            phase = GamePhase.PickWinner;
            status = "Выберите карту на столе";
            yield return new WaitUntil(() => pickedTableCard.Count == players.Count);

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

            foreach (var p in players)
            {
                Card c = GetCard(chosenHandCard[p]);
                hands[p].Remove(c);
                RpcMoveCard(c.netId, foldSlot.position + Vector3.right * (foldCount++ * 0.02f));
                c.faceUp = false;
                c.inFold = true;
                c.ownerNetId = 0;
            }
            situationCard = null;

            // 8. По одной карте каждому
            bool canContinue = true;
            foreach (var p in players)
                canContinue &= DealMemCard(p);

            yield return new WaitForSeconds(0.5f);
            if (!canContinue) break;
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

        int i = hands[p].Count;
        Transform anchor = p.slot == 0 ? handSlotA : handSlotB;
        Vector3 pos = anchor.position + Vector3.right * ((i - 1) * cardSpacing);

        Card c = SpawnCard(CardDeck.Mem, memDeck[0], p.netId, pos);
        memDeck.RemoveAt(0);
        hands[p].Add(c);
        return true;
    }

    Card SpawnCard(CardDeck deck, int index, uint owner, Vector3 pos)
    {
        Card c = Instantiate(cardPrefab, pos, Quaternion.identity);
        c.inFold = false;
        c.deck = deck;
        c.cardIndex = index;
        c.faceUp = false;
        c.ownerNetId = owner;
        NetworkServer.Spawn(c.gameObject);
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
        c.selectedBy = p.netId; // подсветка выбранного
    }

    public void ServerPickTableCard(NetPlayer p, uint cardNetId)
    {
        if (phase != GamePhase.PickWinner || pickedTableCard.ContainsKey(p)) return;
        Card c = GetCard(cardNetId);
        if (c == null || !tableCards.Contains(c)) return;

        pickedTableCard[p] = cardNetId;
        c.selectedBy = p.netId;
    }

    [ClientRpc]
    void RpcMoveCard(uint cardNetId, Vector3 pos)
    {
        if (NetworkClient.spawned.TryGetValue(cardNetId, out var ni))
            ni.transform.position = pos;
    }
}
