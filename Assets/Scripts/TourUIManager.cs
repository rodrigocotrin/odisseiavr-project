using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// RESPONSABILIDADE: Controlar todos os elementos visuais (UI) da cena do Tour.
/// (Textos, Botões, Cores de Feedback, Tela de Fade).
/// Não sabe a lógica do jogo, apenas exibe o que o TourManager manda.
/// </summary>
public class TourUIManager : MonoBehaviour
{
    [Header("Referências da UI")]
    [Tooltip("O componente de texto para exibir a pergunta do quiz.")]
    public TextMeshProUGUI questionTextUI;
    [Tooltip("A lista de botões que servirão como opções de resposta.")]
    public List<Button> answerButtons;
    
    // [NOVO] Referência para o botão de Menu
    [Tooltip("Botão para retornar ao Lobby (Menu).")]
    public Button menuButton; 
    
    // --- LÓGICA DE FADE OPCIONAL ---
    [Tooltip("Uma imagem preta (Image UI) para o fade. (OPCIONAL)")]
    public Image fadeScreen;

    [Header("Configurações de Feedback Visual")]
    [Tooltip("Cor que o botão de resposta assume ao acertar.")]
    public Color correctColor = new Color(0.1f, 0.7f, 0.2f);
    [Tooltip("Cor que o botão de resposta assume ao errar.")]
    public Color incorrectColor = new Color(0.8f, 0.2f, 0.1f);
    [Tooltip("Cor padrão dos botões de resposta.")]
    public Color normalColor = Color.white;

    // Evento para notificar o TourManager quando um botão de RESPOSTA for clicado
    public event System.Action<int> OnAnswerButtonClicked;
    
    // [NOVO] Evento para notificar o TourManager quando o botão de MENU for clicado
    public event System.Action OnMenuButtonClicked;

    // Propriedade pública para o TourManager saber se pode pausar no fade
    public bool HasFadeScreen => fadeScreen != null;

    void Start()
    {
        // --- LÓGICA DE FADE OPCIONAL ---
        if (fadeScreen == null)
        {
            Debug.LogWarning("A 'Fade Screen' (Image) não foi atribuída no TourUIManager. O efeito de fade será desativado.");
        }
        else
        {
            // Garante que a tela comece PRETA.
            Color tempColor = fadeScreen.color;
            tempColor.a = 1.0f; // Começa 100% opaco (preto)
            fadeScreen.color = tempColor;
            fadeScreen.gameObject.SetActive(true); // Começa ATIVADO para cobrir o carregamento
        }
        // --- FIM DA LÓGICA ---

        // Configura os listeners dos botões
        InitializeButtonListeners();
    }

    /// <summary>
    /// Adiciona os listeners aos botões de resposta e ao botão de menu.
    /// [MODIFICADO]
    /// </summary>
    private void InitializeButtonListeners()
    {
        // Listeners dos botões de resposta
        for (int i = 0; i < answerButtons.Count; i++)
        {
            int index = i; // "Captura" o índice para o delegate (lambda)
            if (answerButtons[i] != null)
            {
                answerButtons[i].onClick.AddListener(() => OnAnswerButtonClicked?.Invoke(index));
            }
        }
        
        // [NOVO] Listener do botão de menu
        if (menuButton != null)
        {
            menuButton.onClick.AddListener(() => OnMenuButtonClicked?.Invoke());
        }
        else
        {
            Debug.LogWarning("O 'menuButton' não foi atribuído no TourUIManager.");
        }
    }

    /// <summary>
    /// Atualiza a UI do quiz com os dados de um novo desafio.
    /// [MODIFICADO]
    /// </summary>
    public void ApresentarDesafio(Desafio desafio)
    {
        questionTextUI.text = desafio.questionText;

        // Atualiza os botões de resposta
        for (int i = 0; i < answerButtons.Count; i++)
        {
            if (i < desafio.answers.Count)
            {
                answerButtons[i].gameObject.SetActive(true);
                answerButtons[i].GetComponent<Image>().color = normalColor;
                answerButtons[i].interactable = true;

                var tmproText = answerButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                if (tmproText != null)
                    tmproText.text = desafio.answers[i];
            }
            else
            {
                answerButtons[i].gameObject.SetActive(false);
            }
        }
        
        // [NOVO] Garante que o botão de menu também seja ativado/habilitado
        if (menuButton != null)
        {
            menuButton.gameObject.SetActive(true);
            menuButton.interactable = true;
        }
    }

    /// <summary>
    /// Desativa a interatividade de todos os botões visíveis (respostas E menu).
    /// [MODIFICADO]
    /// </summary>
    public void SetAllButtonsInteractable(bool isInteractable, int answerCount)
    {
        // Desativa/Ativa botões de resposta
        for(int i = 0; i < answerCount; i++)
        {
            if (i < answerButtons.Count && answerButtons[i] != null) 
                answerButtons[i].interactable = isInteractable;
        }
        
        // [NOVO] Desativa/Ativa botão de menu
        if (menuButton != null)
            menuButton.interactable = isInteractable;
    }

    /// <summary>
    /// Aplica cor de feedback a um botão de RESPOSTA específico.
    /// (Este método não precisa mudar, pois o botão de menu não recebe feedback)
    /// </summary>
    public void SetButtonFeedback(int buttonIndex, Color color)
    {
        if (buttonIndex < 0 || buttonIndex >= answerButtons.Count) return;
        answerButtons[buttonIndex].GetComponent<Image>().color = color;
    }

    /// <summary>
    /// Reseta todos os botões para o estado padrão (para nova tentativa).
    /// [MODIFICADO]
    /// </summary>
    public void ResetButtonsToNormal(int answerCount)
    {
        // Reseta botões de resposta
        for (int i = 0; i < answerCount; i++)
        {
             if(i < answerButtons.Count && answerButtons[i].gameObject.activeInHierarchy) {
                answerButtons[i].GetComponent<Image>().color = normalColor;
                answerButtons[i].interactable = true;
             }
        }
        
        // [NOVO] Reseta (habilita) botão de menu
        if (menuButton != null)
        {
            menuButton.interactable = true;
        }
    }

    // --- CORROTINAS DE FADE (COM LÓGICA OPCIONAL) ---
    // (Não precisam de modificação)

    public IEnumerator FadeOut(float fadeDuration)
    {
        if (fadeScreen == null) yield break; // Pula se não houver tela de fade

        Color currentColor = Color.black;
        float startAlpha = fadeScreen.gameObject.activeInHierarchy ? fadeScreen.color.a : 0f;
        float targetAlpha = 1f;
        float timer = 0f;

        fadeScreen.gameObject.SetActive(true);

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / fadeDuration);
            currentColor.a = Mathf.Lerp(startAlpha, targetAlpha, progress);
            fadeScreen.color = currentColor;
            yield return null;
        }
        currentColor.a = targetAlpha;
        fadeScreen.color = currentColor;
    }

    public IEnumerator FadeIn(float fadeDuration)
    {
        if (fadeScreen == null) yield break; // Pula se não houver tela de fade

        Color currentColor = Color.black;
        float startAlpha = 1f;
        float targetAlpha = 0f;
        float timer = 0f;

        fadeScreen.gameObject.SetActive(true);

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / fadeDuration);
            currentColor.a = Mathf.Lerp(startAlpha, targetAlpha, progress);
            fadeScreen.color = currentColor;
            yield return null;
        }
        currentColor.a = targetAlpha;
        fadeScreen.color = currentColor;
        fadeScreen.gameObject.SetActive(false);
    }
}