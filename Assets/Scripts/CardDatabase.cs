using UnityEngine;

public enum CardDeck { Mem, Situation }

[CreateAssetMenu(menuName = "CardGame/CardDatabase")]
public class CardDatabase : ScriptableObject
{
    [Header("Рубашки")]
    public Sprite memBack;
    public Sprite situationBack;
    public Sprite foldBack; // рубашка карт, ушедших в Fold

    [Header("Лицевые стороны")]
    public Sprite[] memFronts;      // по индексам раздачи
    public Sprite[] situationFronts;

    public Sprite GetFront(CardDeck deck, int index) =>
        deck == CardDeck.Mem ? memFronts[index] : situationFronts[index];

    public Sprite GetBack(CardDeck deck) =>
        deck == CardDeck.Mem ? memBack : situationBack;

    public int GetDeckSize(CardDeck deck) =>
        deck == CardDeck.Mem ? memFronts.Length : situationFronts.Length;
}
