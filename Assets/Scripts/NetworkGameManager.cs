using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mirror;

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance;

    [Header("Позиции в сцене")]
    public Transform memDeckPos;
    public Transform situationDeckPos;
    public Transform foldMemPos;
    public Transform foldSituationPos;
    public Transform tableCenter;
    public Transform[] playerHandPos;

    [Header("Спрайты")]
    public Sprite backside;
    public Sprite[] memSprites;
    public Sprite[] situationSprites;

    [Header("Префаб карты")]
    public GameObject cardPrefab;

    [Header("Настройки")]
    public float cardSpeed = 50f;
    public float handSpacing = 1.2f;
    public float tableSpacing = 1.8f;
    public int startingHandSize = 3;   // 3 × 4 игрока = 12 карт
    public int playerCount = 4;

    [Header("UI")]
    public UnityEngine.UI.Text promptText;
    public UnityEngine.UI.Text[] playerScoreTexts;

    // Игроки
    private NetworkPlayer[] players;
    private int connectedPlayers = 0;
    private int[] playerScores;
    private string[] playerNames = { "Игрок 1", "Игрок 2", "Игрок 3", "Игрок 4" };
    private List<List<Card>> playerHands;

    // Колоды
    private List<Card> memDeck = new List<Card>();
    private List<Card> situationDeck = new List<Card>();
    private List<Card> foldMem = new List<Card>();
    private List<Card> foldSituation = new List<Card>();

    // Стол
    private List<Card> tableCards = new List<Card>();
    private int[] tableCardOwners;
    private Card situationCard;

    // Выбор
    private uint? selectedCardNetId;
    private int currentActor = -1;
    private enum Phase { None, SelectCard, Vote }
    private Phase currentPhase = Phase.None;

    private int sortCounter = 10;

    public UnityEngine.UI.Text debugText; // перетащи DebugText в инспекторе

    void Log(string msg)
    {
        if (debugText != null)
            debugText.text += msg + "\n";
        else
            Debug.Log(msg);
    }

    void Awake()
    {
        Instance = this;
        players = new NetworkPlayer[playerCount];
        playerScores = new int[playerCount];
        playerHands = new List<List<Card>>();
        for (int i = 0; i < playerCount; i++)
            playerHands.Add(new List<Card>());
    }

    // ===================== ПОДКЛЮЧЕНИЕ =====================

    public void OnPlayerConnected(NetworkPlayer player)
    {
        if (connectedPlayers >= playerCount) return;

        players[player.playerIndex] = player;
        connectedPlayers++;
        RpcUpdatePrompt($"Игрок {player.playerIndex + 1} подключился ({connectedPlayers}/{playerCount})");

        if (connectedPlayers == playerCount && isServer)
            StartCoroutine(GameLoop());
    }

    // ===================== ИГРОВОЙ ЦИКЛ =====================

    IEnumerator GameLoop()
    {
        Log("Игра началась!");
        RpcUpdatePrompt("Игра началась!");
        yield return new WaitForSeconds(1f);

        BuildDecks();

        // Шаг 1: раздача
        for (int p = 0; p < playerCount; p++)
            yield return DealToPlayer(p, startingHandSize);

        // Основной цикл
        while (situationDeck.Count > 0)
        {
            // Проверяем, есть ли у всех карты
            bool canPlay = true;
            for (int p = 0; p < playerCount; p++)
                if (playerHands[p].Count == 0) canPlay = false;
            if (!canPlay) break;

            // Шаг 2: Situation в центр
            yield return DealSituationToCenter();

            // Шаг 3: каждый выбирает карту из руки
            tableCards.Clear();
            tableCardOwners = new int[playerCount];

            for (int p = 0; p < playerCount; p++)
                yield return PlayerSelectCard(p);

            // Шаг 5: голосование
            int[] votes = new int[playerCount];
            for (int p = 0; p < playerCount; p++)
                yield return PlayerVote(p, votes);

            // Шаг 6: подсчёт
            int maxVotes = 0, winner = -1;
            bool tie = false;
            for (int p = 0; p < playerCount; p++)
            {
                if (votes[p] > maxVotes) { maxVotes = votes[p]; winner = p; tie = false; }
                else if (votes[p] == maxVotes && maxVotes > 0) tie = true;
            }

            if (tie || maxVotes == 0)
                RpcUpdatePrompt("Ничья! Никто не получает очко.");
            else
            {
                playerScores[winner]++;
                RpcUpdatePrompt($"{playerNames[winner]} получает очко!");
            }
            RpcUpdateScores(playerScores);
            yield return new WaitForSeconds(2f);

            // Шаг 7: сброс
            foreach (var c in tableCards)
            {
                c.SetFaceUp(false);
                c.SetSortingOrder(0);
                for (int p = 0; p < playerCount; p++)
                    players[p].TargetSetCardFace(c.netIdentity.netId, false);
            }
            yield return MoveAllTo(tableCards, foldMemPos.position);
            foldMem.AddRange(tableCards);
            tableCards.Clear();

            situationCard.SetFaceUp(false);
            situationCard.SetSortingOrder(0);
            yield return MoveCardAndWait(situationCard, foldSituationPos.position);
            foldSituation.Add(situationCard);
            situationCard = null;

            // Шаг 8: по 1 карте каждому
            for (int p = 0; p < playerCount; p++)
            {
                if (memDeck.Count > 0)
                    yield return DealToPlayer(p, 1);
            }
        }

        DeclareWinner();
    }

    // ===================== СОЗДАНИЕ КОЛОД =====================

    void BuildDecks()
    {
        for (int i = 0; i < memSprites.Length; i++)
        {
            var go = Instantiate(cardPrefab, memDeckPos.position, Quaternion.identity);
            go.transform.localScale = Vector3.one * 2f;
            go.transform.position = FixZ(go.transform.position);

            Card card = go.GetComponent<Card>();
            card.Init(Card.CardType.Mem, i, i);
            card.moveSpeed = cardSpeed;
            card.SetSortingOrder(0);

            NetworkServer.Spawn(go);
            memDeck.Add(card);
        }

        for (int i = 0; i < situationSprites.Length; i++)
        {
            var go = Instantiate(cardPrefab, situationDeckPos.position, Quaternion.identity);
            go.transform.localScale = Vector3.one * 2f;
            go.transform.position = FixZ(go.transform.position);

            Card card = go.GetComponent<Card>();
            card.Init(Card.CardType.Situation, i, i);
            card.moveSpeed = cardSpeed;
            card.SetSortingOrder(0);

            NetworkServer.Spawn(go);
            situationDeck.Add(card);
        }

        ShuffleDeck(memDeck);
        ShuffleDeck(situationDeck);
        Log($"Колоды созданы. Mem: {memDeck.Count}, Situation: {situationDeck.Count}");
    }

    Vector3 FixZ(Vector3 v) => new Vector3(v.x, v.y, 0f);

    void ShuffleDeck(List<Card> deck)
    {
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    // ===================== РАЗДАЧА =====================

    IEnumerator DealToPlayer(int playerIndex, int count)
    {
        int canDeal = Mathf.Min(count, memDeck.Count);
        var hand = playerHands[playerIndex];

        for (int i = 0; i < canDeal; i++)
        {
            Card card = memDeck[0];
            memDeck.RemoveAt(0);
            hand.Add(card);

            card.SetFaceUp(false); // все видят рубашку
            card.SetSortingOrder(sortCounter++);

            // Только владелец видит лицо
            players[playerIndex].TargetSetCardFace(card.netIdentity.netId, true);

            Vector3 pos = GetHandPosition(playerIndex, hand.Count - 1, hand.Count);
            yield return MoveCardAndWait(card, pos);
        }
        ArrangePlayerHand(playerIndex);
    }

    IEnumerator DealSituationToCenter()
    {
        if (situationDeck.Count == 0) yield break;

        situationCard = situationDeck[0];
        situationDeck.RemoveAt(0);
        situationCard.SetFaceUp(true);
        situationCard.SetSortingOrder(sortCounter++);

        Vector3 pos = tableCenter.position + new Vector3(0, 2.5f, 0);
        yield return MoveCardAndWait(situationCard, pos);
    }

    // ===================== ВЫБОР КАРТЫ =====================

    IEnumerator PlayerSelectCard(int playerIndex)
    {
        RpcUpdatePrompt($"{playerNames[playerIndex]}, выберите карту из руки");
        RpcClearClickable();

        uint[] handIds = playerHands[playerIndex]
            .Select(c => c.netIdentity.netId).ToArray();
        players[playerIndex].TargetEnableCards(handIds);

        selectedCardNetId = null;
        currentActor = playerIndex;
        currentPhase = Phase.SelectCard;

        yield return new WaitUntil(() => selectedCardNetId.HasValue);

        Card selected = GetCard(selectedCardNetId.Value);
        playerHands[playerIndex].Remove(selected);
        ArrangePlayerHand(playerIndex);

        int slot = tableCards.Count;
        tableCards.Add(selected);
        tableCardOwners[slot] = playerIndex;

        selected.SetFaceUp(true); // теперь все видят
        selected.SetSortingOrder(sortCounter++);

        Vector3 pos = GetTablePosition(playerIndex);
        yield return MoveCardAndWait(selected, pos);
        RpcClearClickable();
    }

    // ===================== ГОЛОСОВАНИЕ =====================

    IEnumerator PlayerVote(int voterIndex, int[] votes)
    {
        RpcUpdatePrompt($"{playerNames[voterIndex]}, проголосуйте за чужую карту");

        var voteable = new List<uint>();
        for (int i = 0; i < tableCards.Count; i++)
            if (tableCardOwners[i] != voterIndex)
                voteable.Add(tableCards[i].netIdentity.netId);

        RpcClearClickable();
        players[voterIndex].TargetEnableCards(voteable.ToArray());

        selectedCardNetId = null;
        currentActor = voterIndex;
        currentPhase = Phase.Vote;

        yield return new WaitUntil(() => selectedCardNetId.HasValue);

        Card voted = GetCard(selectedCardNetId.Value);
        int idx = tableCards.IndexOf(voted);
        if (idx >= 0) votes[tableCardOwners[idx]]++;

        RpcClearClickable();
    }

    // ===================== ОБРАБОТКА КОМАНД =====================

    public void HandleCardSelection(int playerIndex, uint cardNetId)
    {
        if (playerIndex != currentActor) return;
        if (!selectedCardNetId.HasValue)
            selectedCardNetId = cardNetId;
    }

    Card GetCard(uint netId)
    {
        if (NetworkClient.spawned.TryGetValue(netId, out var identity))
            return identity.GetComponent<Card>();
        return null;
    }

    // ===================== ПЕРЕМЕЩЕНИЕ =====================

    IEnumerator MoveCardAndWait(Card card, Vector3 target)
    {
        bool arrived = false;
        card.MoveTo(target, () => arrived = true);
        yield return new WaitUntil(() => arrived);
    }

    IEnumerator MoveAllTo(List<Card> cards, Vector3 baseTarget)
    {
        if (cards.Count == 0) yield break;

        int arrived = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            Vector3 offset = new Vector3(i * 0.15f, i * 0.15f, 0);
            cards[i].MoveTo(baseTarget + offset, () => arrived++);
        }
        yield return new WaitUntil(() => arrived >= cards.Count);
    }

    // ===================== РАСКЛАДКА =====================

    void ArrangePlayerHand(int playerIndex)
    {
        var hand = playerHands[playerIndex];
        for (int i = 0; i < hand.Count; i++)
        {
            Vector3 pos = GetHandPosition(playerIndex, i, hand.Count);
            hand[i].MoveTo(pos);
        }
    }

    Vector3 GetHandPosition(int playerIndex, int cardIndex, int total)
    {
        Vector3 center = playerHandPos[playerIndex].position;
        float offset = (cardIndex - (total - 1) / 2f) * handSpacing;

        if (playerIndex == 0 || playerIndex == 2)
            return FixZ(center + new Vector3(offset, 0, 0));
        else
            return FixZ(center + new Vector3(0, offset, 0));
    }

    Vector3 GetTablePosition(int playerIndex)
    {
        return playerIndex switch
        {
            0 => FixZ(tableCenter.position + new Vector3(0, -tableSpacing, 0)),
            1 => FixZ(tableCenter.position + new Vector3(tableSpacing, 0, 0)),
            2 => FixZ(tableCenter.position + new Vector3(0, tableSpacing, 0)),
            3 => FixZ(tableCenter.position + new Vector3(-tableSpacing, 0, 0)),
            _ => FixZ(tableCenter.position)
        };
    }

    // ===================== RPC =====================

    [ClientRpc]
    void RpcUpdatePrompt(string text)
    {
        if (promptText != null)
            promptText.text = text;
        Log("[PROMPT] " + text);
    }

    [ClientRpc]
    void RpcClearClickable()
    {
        foreach (var kv in NetworkClient.spawned)
        {
            var card = kv.Value.GetComponent<Card>();
            if (card != null) card.IsClickable = false;
        }
    }

    [ClientRpc]
    void RpcUpdateScores(int[] scores)
    {
        if (playerScoreTexts == null) return;
        for (int p = 0; p < scores.Length && p < playerScoreTexts.Length; p++)
        {
            if (playerScoreTexts[p] != null)
                playerScoreTexts[p].text = $"{playerNames[p]}: {scores[p]}";
        }
    }

    // ===================== ФИНАЛ =====================

    void DeclareWinner()
    {
        int maxScore = 0, winner = -1;
        bool tie = false;

        for (int p = 0; p < playerCount; p++)
        {
            if (playerScores[p] > maxScore)
            {
                maxScore = playerScores[p];
                winner = p;
                tie = false;
            }
            else if (playerScores[p] == maxScore && maxScore > 0)
                tie = true;
        }

        string result;
        if (tie || winner == -1)
            result = "Ничья!";
        else
            result = $"Победил {playerNames[winner]}!";

        string scores = "";
        for (int p = 0; p < playerCount; p++)
            scores += $"{playerNames[p]}: {playerScores[p]}  ";

        RpcUpdatePrompt($"{result}\n{scores}");
        Log($"=== ИГРА ОКОНЧЕНА ===\n{result}\n{scores}");
    }
}