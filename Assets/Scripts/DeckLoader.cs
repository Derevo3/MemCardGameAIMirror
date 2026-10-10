using UnityEngine;
using System.Collections.Generic;

public class DeckLoader : MonoBehaviour
{
    [Header("Папки в Resources (без слова Resources в пути)")]
    [Tooltip("Путь относительно Assets/Resources")]
    public string memCardsPath = "MemCards";
    public string situationCardsPath = "SituationCards";

    public Sprite[] memCards;
    public Sprite[] situationCards;

    private void Awake()
    {
        LoadCards();
    }

    public void LoadCards()
    {
        // Загрузка всех спрайтов из папки
        memCards = Resources.LoadAll<Sprite>(memCardsPath);
        situationCards = Resources.LoadAll<Sprite>(situationCardsPath);

        Debug.Log($"Загружено карт Mem: {memCards.Length}");
        Debug.Log($"Загружено карт Situation: {situationCards.Length}");

        if (memCards.Length == 0)
            Debug.LogWarning("Не найдено ни одной карты в папке Resources/" + memCardsPath);
        if (situationCards.Length == 0)
            Debug.LogWarning("Не найдено ни одной карты в папке Resources/" + situationCardsPath);
    }
}
