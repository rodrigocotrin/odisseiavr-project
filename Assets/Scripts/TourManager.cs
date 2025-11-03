using UnityEngine;
using UnityEngine.UI; // Necessário para Image e Button
using System.Collections;
using System.Collections.Generic;
using TMPro; // Necessário para TextMeshProUGUI

// [NOVO] Necessário para carregar a cena do Lobby
using UnityEngine.SceneManagement; 

// [IMPORTANTE] Adiciona a namespace do XR Interaction Toolkit
// Garanta que o pacote 'XR Interaction Toolkit' esteja instalado no seu projeto.
using UnityEngine.XR.Interaction.Toolkit; 

// --- ESTRUTURAS DE DADOS PARA O RUNTIME (O QUE O JOGO USA) ---

/// <summary>
/// Estrutura que representa um desafio durante a execução do jogo.
/// </summary>
public class Desafio
{
    public Material panoramaMaterial;    // Material com a textura 360°
    public float initialYRotation;       // Rotação inicial no eixo Y para este panorama
    public string questionText;          // Texto da pergunta do quiz
    public List<string> answers;         // Lista de respostas possíveis
    public int correctAnswerIndex;       // Índice da resposta correta
}

/// <summary>
/// Estrutura que representa um local (ex: Coliseu) com todos os seus desafios.
/// </summary>
public class DadosLocal
{
    public string locationName;          // Nome do local
    public AudioClip backgroundMusic;    // Trilha sonora do local
    public List<Desafio> desafios;       // Lista de desafios do local
}

// --- ESTRUTURAS DE DADOS PARA O JSON (O QUE O ARQUIVO CONTÉM) ---
// Essas classes espelham a estrutura do arquivo JSON para permitir a desserialização.

[System.Serializable]
public class DesafioJson
{
    public string panoramaMaterialPath;
    public float initialYRotation;
    public string questionText;
    public List<string> answers;
    public int correctAnswerIndex;
}

[System.Serializable]
public class DadosLocalJson
{
    public string locationName;
    public string backgroundMusicPath;
    // IMPORTANTE: Este campo deve existir para ser compatível com o JSON
    // usado pelo LobbyManager, mesmo que o TourManager não o utilize.
    public string mapImagePath;
    public List<DesafioJson> desafios;
}

[System.Serializable]
public class TourDataJson
{
    public List<DadosLocalJson> locais;
}

/// <summary>
/// Gerenciador principal do Tour.
/// Carrega dados do JSON, controla o quiz, a música, os panoramas
/// e as transições entre os locais.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class TourManager : MonoBehaviour
{
    [Header("Arquivo de Conteúdo")]
    [Tooltip("Arraste aqui o arquivo JSON que contém os dados dos tours.")]
    public TextAsset tourDataJson;

    [Header("Referências da Cena")]
    [Tooltip("O objeto Renderer da esfera que exibirá o panorama 360°.")]
    public Renderer panoramaSphereRenderer;
    [Tooltip("O componente de texto para exibir a pergunta do quiz.")]
    public TextMeshProUGUI questionTextUI;
    [Tooltip("A lista de botões que servirão como opções de resposta.")]
    public List<Button> answerButtons;
    [Tooltip("Uma imagem preta (Image UI) que cobre a tela para o efeito de fade.")]
    public Image fadeScreen;

    // --- [NOVO CAMPO] ---
    [Header("Configurações de Cena")]
    [Tooltip("O nome exato da sua cena do Lobby (ex: 'LobbyScene').")]
    public string lobbySceneName = "LobbyScene";
    // --------------------

    [Header("Configurações de Feedback")]
    [Tooltip("Cor que o botão de resposta assume ao acertar.")]
    public Color correctColor = new Color(0.1f, 0.7f, 0.2f);
    [Tooltip("Cor que o botão de resposta assume ao errar.")]
    public Color incorrectColor = new Color(0.8f, 0.2f, 0.1f);
    [Tooltip("Cor padrão dos botões de resposta.")]
    public Color normalColor = Color.white;
    [Tooltip("Tempo (em segundos) que o feedback de cor permanece na tela.")]
    public float feedbackDelay = 1.5f;

    [Header("Configurações de Transição de Local")]
    [Tooltip("Som a ser tocado ao completar todos os desafios de um local.")]
    public AudioClip locationVictorySound;
    [Tooltip("Tempo (em segundos) que a tela permanece preta durante a transição.")]
    public float waitOnBlackScreenDelay = 1.0f;
    [Tooltip("Duração (em segundos) do efeito de fade in/out.")]
    public float fadeDuration = 0.8f;

    [Header("Configurações de Áudio")]
    [Tooltip("Volume da música de fundo (0 a 1).")]
    [Range(0f, 1f)] public float backgroundMusicVolume = 0.5f;
    [Tooltip("Volume dos efeitos sonoros (0 a 1).")]
    [Range(0f, 1f)] public float sfxVolume = 1.0f;
    [Tooltip("Som de resposta correta.")]
    public AudioClip correctAnswerSound;
    [Tooltip("Som de resposta incorreta.")]
    public AudioClip incorrectAnswerSound;
    
    // --- Variáveis Privadas de Controle ---
    private List<DadosLocal> locais = new List<DadosLocal>();
    private int currentLocalIndex = 0; // Este valor será sobrescrito pelo GameSettings
    private int currentDesafioIndex = 0;
    private bool isAnswering = false; // Trava para evitar cliques múltiplos
    private AudioSource audioSource;

    /// <summary>
    /// Chamado quando o script é inicializado.
    /// Agora também desativa o movimento do jogador.
    /// </summary>
    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        
        // Validação crítica para o Fade
        if (fadeScreen == null) {
             Debug.LogError("ERRO CRÍTICO: A 'Fade Screen' não foi atribuída no Inspector! O fade não funcionará.");
             enabled = false; // Desativa o script se o fade não estiver configurado
             return;
        }
        
        // Garante que a tela comece PRETA.
        Color tempColor = fadeScreen.color;
        tempColor.a = 1.0f; // Começa 100% opaco (preto)
        fadeScreen.color = tempColor;
        fadeScreen.gameObject.SetActive(true); // Começa ATIVADO para cobrir o carregamento
        
        // --- [AÇÃO DE SEGURANÇA] ---
        // Desativa programaticamente os componentes de input de movimento.
        // Isso garante que o movimento não ocorra, mesmo que o Character Controller
        // seja reativado acidentalmente.
        DisablePlayerMovement();
        // -------------------------
        
        // 1. Configura os botões de resposta
        for (int i = 0; i < answerButtons.Count; i++)
        {
            int index = i; // "Captura" o índice para o delegate (lambda)
            if (answerButtons[i] != null) {
                answerButtons[i].onClick.AddListener(() => CheckAnswer(index));
            }
        }

        // 2. LÓGICA DE INTEGRAÇÃO COM O LOBBY (GameSettings)
        GameSettings settings = GameSettings.Instance;
        if (settings != null)
        {
            currentLocalIndex = settings.selectedLocationIndex;
            Destroy(settings.gameObject);
        }
        else
        {
            currentLocalIndex = 0;
            Debug.LogWarning("GameSettings não encontrado. Iniciando do local padrão (Índice 0).");
        }

        // 3. Inicia a "Master Coroutine" que fará o carregamento
        //    assíncrono dos assets e depois iniciará o tour.
        StartCoroutine(InitializeTour());
    }

    /// <summary>
    /// [FUNÇÃO DE SEGURANÇA]
    /// Encontra e desativa componentes de movimento (como Continuous Move ou Teleport)
    /// no XR Origin para garantir que a experiência seja estacionária.
    /// </summary>
    void DisablePlayerMovement()
    {
        // Tenta encontrar o componente base de movimento contínuo (analógico)
        var moveProvider = FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.ContinuousMoveProviderBase>();
        if (moveProvider != null)
        {
            moveProvider.enabled = false;
            Debug.Log("ContinuousMoveProviderBase desativado para a cena do Tour.");
        }
        else
        {
            Debug.LogWarning("Nenhum 'ContinuousMoveProviderBase' encontrado para desativar.");
        }

        // Por segurança, desativa também o teleporte, caso exista
        var teleportProvider = FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>();
        if (teleportProvider != null)
        {
            teleportProvider.enabled = false;
            Debug.Log("TeleportationProvider desativado para a cena do Tour.");
        }
        else
        {
            Debug.LogWarning("Nenhum 'TeleportationProvider' encontrado para desativar.");
        }
    }


    /// <summary>
    /// [CORROTINA]
    /// Orquestra a inicialização: primeiro carrega os dados (async)
    /// e depois inicia a transição do primeiro local (async).
    /// </summary>
    private IEnumerator InitializeTour()
    {
        // 1. Espera o carregamento de todos os assets do JSON terminar.
        yield return StartCoroutine(LoadTourDataFromJSONAsync());
        
        // 2. Agora que tudo está na memória, inicia o primeiro tour.
        if (locais.Count > 0)
        {
            // O 'true' (isFirstLoad) garante que ele faça o Fade-IN inicial
            // vindo da cena de Lobby (que estava preta).
            StartCoroutine(TransitionToLocal(currentLocalIndex, true));
        }
        else
        {
            Debug.LogError("Nenhum local foi carregado do JSON. Verifique o arquivo.");
            // Se falhar, pelo menos remove a tela preta
            yield return StartCoroutine(FadeIn()); 
        }
    }

    /// <summary>
    /// [CORROTINA] Usa Resources.LoadAsync.
    /// 
    /// Lê o arquivo JSON, converte os dados para as estruturas de runtime
    /// e carrega os assets (Materiais e Áudios) da pasta 'Resources' ASSINCRONAMENTE.
    /// </summary>
    IEnumerator LoadTourDataFromJSONAsync()
    {
        if (tourDataJson == null)
        {
            Debug.LogError("ERRO CRÍTICO: O arquivo 'tourDataJson' não foi atribuído no Inspector!");
            enabled = false;
            yield break; // Para a corrotina
        }

        // Desserializa o texto do JSON (isso é rápido)
        TourDataJson dataFromJson = JsonUtility.FromJson<TourDataJson>(tourDataJson.text);

        // Limpa a lista de locais em runtime antes de preenchê-la
        locais.Clear();

        // Itera por cada "local" lido do JSON
        foreach (var localJson in dataFromJson.locais)
        {
            DadosLocal novoLocal = new DadosLocal
            {
                locationName = localJson.locationName,
                desafios = new List<Desafio>()
            };
            
            // --- Carregamento Assíncrono do Áudio ---
            if (!string.IsNullOrEmpty(localJson.backgroundMusicPath))
            {
                ResourceRequest audioRequest = Resources.LoadAsync<AudioClip>(localJson.backgroundMusicPath);
                yield return audioRequest; // Espera o carregamento terminar

                if (audioRequest.asset != null)
                {
                    novoLocal.backgroundMusic = audioRequest.asset as AudioClip;
                }
                else
                {
                    Debug.LogWarning($"Asset de Áudio não encontrado em 'Resources/{localJson.backgroundMusicPath}' para o local '{novoLocal.locationName}'");
                }
            }
            
            // Itera por cada "desafio" dentro do local
            foreach(var desafioJson in localJson.desafios)
            {
                Desafio novoDesafio = new Desafio
                {
                    // Dados rápidos (não precisam de load)
                    initialYRotation = desafioJson.initialYRotation, 
                    questionText = desafioJson.questionText,
                    answers = desafioJson.answers,
                    correctAnswerIndex = desafioJson.correctAnswerIndex
                };
                
                // --- Carregamento Assíncrono do Material ---
                if (!string.IsNullOrEmpty(desafioJson.panoramaMaterialPath))
                {
                    ResourceRequest materialRequest = Resources.LoadAsync<Material>(desafioJson.panoramaMaterialPath);
                    yield return materialRequest; // Espera o carregamento terminar

                    if (materialRequest.asset != null)
                    {
                        novoDesafio.panoramaMaterial = materialRequest.asset as Material;
                    }
                    else
                    {
                         Debug.LogWarning($"Asset de Material não encontrado em 'Resources/{desafioJson.panoramaMaterialPath}' para o local '{novoLocal.locationName}'");
                    }
                }
                
                novoLocal.desafios.Add(novoDesafio);
            }
            
            locais.Add(novoLocal);
        }
    }

    /// <summary>
    /// Configura a cena para o local especificado, incluindo música de fundo.
    /// </summary>
    void CarregarDadosDoLocal(int localIndex)
    {
        if(locais.Count == 0 || localIndex >= locais.Count) {
             Debug.LogError("Tentativa de carregar um local inválido.");
             return;
        }
        currentLocalIndex = localIndex;
        currentDesafioIndex = 0; // Reseta para o primeiro desafio do novo local

        // Toca a música de fundo do local.
        if (locais[currentLocalIndex].backgroundMusic != null)
        {
            audioSource.clip = locais[currentLocalIndex].backgroundMusic;
            audioSource.volume = backgroundMusicVolume;
            audioSource.loop = true;
            audioSource.Play();
        } else {
            // Se não houver música, para qualquer música que estivesse tocando.
            audioSource.Stop();
        }
        
        // Exibe o primeiro desafio do local.
        ApresentarDesafio();
    }

    /// <summary>
    /// Atualiza a cena com os dados do desafio atual (panorama, rotação, pergunta e respostas).
    /// </summary>
    void ApresentarDesafio()
    {
        // Garante que os índices não estourem
        if (currentLocalIndex >= locais.Count || 
            currentDesafioIndex >= locais[currentLocalIndex].desafios.Count)
        {
            Debug.LogError($"Índice de desafio inválido! Local: {currentLocalIndex}, Desafio: {currentDesafioIndex}");
            // Fallback: Volta ao primeiro local
            StartCoroutine(TransitionToLocal(0, false));
            return;
        }
        
        Desafio desafioAtual = locais[currentLocalIndex].desafios[currentDesafioIndex];

        if (desafioAtual.panoramaMaterial == null)
        {
            Debug.LogError($"Material nulo para o desafio {currentDesafioIndex} do local {locais[currentLocalIndex].locationName}. Verifique o caminho no JSON e se o asset existe em Resources.");
        }
        
        panoramaSphereRenderer.transform.rotation = Quaternion.Euler(0, desafioAtual.initialYRotation, 0);
        panoramaSphereRenderer.material = desafioAtual.panoramaMaterial;
        questionTextUI.text = desafioAtual.questionText;

        // Configura os botões de resposta
        for (int i = 0; i < answerButtons.Count; i++)
        {
            if (i < desafioAtual.answers.Count)
            {
                answerButtons[i].gameObject.SetActive(true); // Ativa o botão
                answerButtons[i].GetComponent<Image>().color = normalColor; // Reseta a cor
                answerButtons[i].interactable = true; // Torna o botão clicável
                
                var tmproText = answerButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                if (tmproText != null)
                    tmproText.text = desafioAtual.answers[i]; // Define o texto da resposta
            }
            else
            {
                answerButtons[i].gameObject.SetActive(false);
            }
        }
        
        isAnswering = false;
    }

    /// <summary>
    /// Chamado quando um botão de resposta é clicado.
    /// </summary>
    public void CheckAnswer(int selectedIndex)
    {
        if (isAnswering) return; 
        isAnswering = true; // Ativa a trava

        Desafio desafioAtual = locais[currentLocalIndex].desafios[currentDesafioIndex];
        
        for(int i = 0; i < desafioAtual.answers.Count; i++) {
             if (answerButtons[i] != null) 
                answerButtons[i].interactable = false;
        }

        if (selectedIndex == desafioAtual.correctAnswerIndex)
        {
            StartCoroutine(HandleCorrectAnswer(answerButtons[selectedIndex]));
        }
        else
        {
            StartCoroutine(HandleIncorrectAnswer(answerButtons[selectedIndex]));
        }
    }
    
    // --- CORROTINAS DE EFEITOS E TRANSIÇÕES ---

    /// <summary>
    /// Escurece a tela (Fade Out).
    /// </summary>
    private IEnumerator FadeOut() {
        Color currentColor = Color.black; // Começa como preto
        float startAlpha = 0f;
        
        if(fadeScreen.gameObject.activeInHierarchy) {
            startAlpha = fadeScreen.color.a;
        }
        
        float targetAlpha = 1f;
        float timer = 0f;

        fadeScreen.gameObject.SetActive(true); // Garante que a tela de fade esteja ativa

        while (timer < fadeDuration) {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / fadeDuration);
            currentColor.a = Mathf.Lerp(startAlpha, targetAlpha, progress);
            fadeScreen.color = currentColor;
            yield return null; // Espera o próximo frame
        }

        currentColor.a = targetAlpha;
        fadeScreen.color = currentColor;
    }

    /// <summary>
    /// Clareia a tela (Fade In).
    /// </summary>
    private IEnumerator FadeIn() {
        Color currentColor = Color.black; // Começa como preto
        float startAlpha = 1f;
        float targetAlpha = 0f;
        float timer = 0f;
        
        fadeScreen.gameObject.SetActive(true); // Garante que a tela de fade esteja ativa

        while (timer < fadeDuration) {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / fadeDuration);
            currentColor.a = Mathf.Lerp(startAlpha, targetAlpha, progress);
            fadeScreen.color = currentColor;
            yield return null; // Espera o próximo frame
        }

        currentColor.a = targetAlpha;
        fadeScreen.color = currentColor;
        
        // Desativa a tela de fade após terminar para não bloquear raios de interação
        fadeScreen.gameObject.SetActive(false); 
    }

    /// <summary>
    /// Gerencia a transição completa entre locais, usando os efeitos de fade.
    /// </summary>
    private IEnumerator TransitionToLocal(int localIndex, bool isFirstLoad = false) {
        
        if (isFirstLoad)
        {
             // Se é o primeiro load, a tela JÁ DEVE ESTAR preta.
             Color tempColor = Color.black;
             tempColor.a = 1;
             fadeScreen.color = tempColor;
             fadeScreen.gameObject.SetActive(true);
             
             yield return new WaitForSeconds(0.2f);
        }
        else
        {
            // Se for uma transição normal (entre locais), fazemos o FadeOut.
            yield return StartCoroutine(FadeOut());
            audioSource.Stop(); // Para a música do local anterior
            
            if (locationVictorySound != null) 
                audioSource.PlayOneShot(locationVictorySound, sfxVolume);
                
            yield return new WaitForSeconds(waitOnBlackScreenDelay);
        }

        // Carrega os dados do novo local (música, primeiro desafio)
        CarregarDadosDoLocal(localIndex);

        // Agora, clareia a tela (FadeIn).
        yield return StartCoroutine(FadeIn());
    }

    /// <summary>
    /// Lógica para quando o usuário acerta a resposta.
    /// [MODIFICADO] Agora verifica se é o último local.
    /// </summary>
    private IEnumerator HandleCorrectAnswer(Button correctButton)
    {
        // Feedback visual e sonoro
        correctButton.GetComponent<Image>().color = correctColor;
        if(correctAnswerSound != null) audioSource.PlayOneShot(correctAnswerSound, sfxVolume); 
        
        yield return new WaitForSeconds(feedbackDelay);

        // Avança para o próximo desafio
        currentDesafioIndex++;
        
        // Verifica se completou todos os desafios do local atual.
        if (currentDesafioIndex >= locais[currentLocalIndex].desafios.Count)
        {
            // --- [LÓGICA DE FIM DE TOUR MODIFICADA] ---
            
            // Verifica se o local ATUAL (que acabou de terminar)
            // é o último local da lista.
            if (currentLocalIndex >= locais.Count - 1)
            {
                // SIM, este era o último local.
                // Inicia a rotina de retorno ao Lobby.
                yield return StartCoroutine(ReturnToLobby());
            }
            else
            {
                // NÃO, ainda há locais. Vamos para o próximo.
                // (Note: Não usamos mais o '%' (módulo) para evitar o loop)
                int proximoLocalIndex = currentLocalIndex + 1;
                
                // Chama a transição normal
                yield return StartCoroutine(TransitionToLocal(proximoLocalIndex, false)); 
            }
            // ---------------------------------------------
        }
        else
        {
            // Não, ainda há desafios. Apenas apresenta o próximo.
            ApresentarDesafio();
        }
    }

    /// <summary>
    /// Lógica para quando o usuário erra a resposta.
    /// </summary>
    private IEnumerator HandleIncorrectAnswer(Button incorrectButton)
    {
        // Feedback visual e sonoro
        incorrectButton.GetComponent<Image>().color = incorrectColor;
        if(incorrectAnswerSound != null) audioSource.PlayOneShot(incorrectAnswerSound, sfxVolume);
        
        yield return new WaitForSeconds(feedbackDelay);
        
        // Reseta as cores e reabilita os botões para uma nova tentativa.
        for (int i = 0; i < locais[currentLocalIndex].desafios[currentDesafioIndex].answers.Count; i++)
        {
             if(answerButtons[i].gameObject.activeInHierarchy) {
                answerButtons[i].GetComponent<Image>().color = normalColor;
                answerButtons[i].interactable = true;
             }
        }
        
        // Libera a trava para o usuário tentar novamente.
        isAnswering = false;
    }
    
    /// <summary>
    /// [NOVA CORROTINA]
    /// Executa o fade out final e carrega a cena do Lobby.
    /// </summary>
    private IEnumerator ReturnToLobby()
    {
        // 1. Escurece a tela
        yield return StartCoroutine(FadeOut());
        audioSource.Stop(); // Para a música do local anterior
        
        // 2. Toca o som de vitória final (pode ser o mesmo som de transição)
        if (locationVictorySound != null) 
            audioSource.PlayOneShot(locationVictorySound, sfxVolume);
            
        // 3. Espera um pouco na tela preta
        yield return new WaitForSeconds(waitOnBlackScreenDelay);

        // 4. Carrega a cena do Lobby de forma assíncrona
        if (string.IsNullOrEmpty(lobbySceneName))
        {
            Debug.LogError("O 'lobbySceneName' não foi definido no Inspector do TourManager! Não é possível voltar ao lobby.");
            yield break;
        }
        
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(lobbySceneName);
        while (!asyncLoad.isDone)
        {
            yield return null; // Espera o carregamento
        }
    }
}