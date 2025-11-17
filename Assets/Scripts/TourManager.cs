using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
// [IMPORTANTE] Adiciona a namespace do XR Interaction Toolkit
using UnityEngine.XR.Interaction.Toolkit; 

/// <summary>
/// RESPONSABILIDADE: Orquestrar o fluxo do tour.
/// Gerencia o estado (local/desafio atual), a lógica do quiz (CheckAnswer),
/// o áudio e as transições de cena.
/// Dá ordens ao TourDataManager e ao TourUIManager.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(TourDataManager))] // Garante que os scripts existam
[RequireComponent(typeof(TourUIManager))]   // no mesmo objeto
public class TourManager : MonoBehaviour
{
    [Header("Componentes (Arraste)")]
    [Tooltip("O script que gerencia o carregamento dos dados.")]
    [SerializeField] private TourDataManager dataManager;
    [Tooltip("O script que gerencia os elementos visuais (UI).")]
    [SerializeField] private TourUIManager uiManager;
    
    [Header("Referências da Cena")]
    [Tooltip("O objeto Renderer da esfera que exibirá o panorama 360°.")]
    public Renderer panoramaSphereRenderer;
    
    [Header("Configurações de Cena")]
    [Tooltip("O nome exato da sua cena do Lobby (ex: 'LobbyScene').")]
    public string lobbySceneName = "LobbyScene";
    
    [Header("Configurações de Feedback")]
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
    private int currentLocalIndex = 0;
    private int currentDesafioIndex = 0;
    private bool isAnswering = false; // Trava para evitar cliques múltiplos
    private AudioSource audioSource;
    private Desafio desafioAtual; // Referência rápida ao desafio atual

    /// <summary>
    /// Chamado quando o script é inicializado.
    /// </summary>
    void Start()
    {
        audioSource = GetComponent<AudioSource>();

        // Tenta auto-encontrar os componentes se não forem arrastados
        if (dataManager == null) dataManager = GetComponent<TourDataManager>();
        if (uiManager == null) uiManager = GetComponent<TourUIManager>();

        // [PASSO 1] Inscreve-se no evento do UIManager.
        // Quando um botão for clicado, o UIManager irá "avisar"
        // o TourManager, chamando a função CheckAnswer.
        uiManager.OnAnswerButtonClicked += CheckAnswer;
        
        // [PASSO 2] Desativa o movimento do jogador.
        DisablePlayerMovement();
        
        // [PASSO 3] Verifica se viemos do Lobby
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

        // [PASSO 4] Inicia a "Master Coroutine"
        StartCoroutine(InitializeTour());
    }

    /// <summary>
    /// Encontra e desativa componentes de movimento (como Continuous Move ou Teleport)
    /// no XR Origin para garantir que a experiência seja estacionária.
    /// </summary>
    void DisablePlayerMovement()
    {
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
    /// Orquestra a inicialização: primeiro carrega os dados
    /// e depois inicia a transição do primeiro local.
    /// </summary>
    private IEnumerator InitializeTour()
    {
        // 1. Espera o DataManager terminar de carregar tudo.
        yield return StartCoroutine(dataManager.LoadTourDataFromJSONAsync());
        
        // Pega uma referência local à lista de locais carregados
        locais = dataManager.Locais;
        
        // 2. Agora que tudo está na memória, inicia o primeiro tour.
        if (locais.Count > 0)
        {
            // O 'true' (isFirstLoad) garante que ele faça o Fade-IN inicial
            StartCoroutine(TransitionToLocal(currentLocalIndex, true));
        }
        else
        {
            Debug.LogError("Nenhum local foi carregado do JSON. Verifique o arquivo.");
            // Se falhar, pelo menos remove a tela preta
            yield return StartCoroutine(uiManager.FadeIn(fadeDuration)); 
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
        currentDesafioIndex = 0; // Reseta para o primeiro desafio

        // Toca a música de fundo
        if (locais[currentLocalIndex].backgroundMusic != null)
        {
            audioSource.clip = locais[currentLocalIndex].backgroundMusic;
            audioSource.volume = backgroundMusicVolume;
            audioSource.loop = true;
            audioSource.Play();
        } else {
            audioSource.Stop();
        }
        
        // Exibe o primeiro desafio do local.
        ApresentarDesafio();
    }

    /// <summary>
    /// Atualiza a cena com os dados do desafio atual.
    /// </summary>
    void ApresentarDesafio()
    {
        if (currentLocalIndex >= locais.Count || 
            currentDesafioIndex >= locais[currentLocalIndex].desafios.Count)
        {
            Debug.LogError($"Índice de desafio inválido! Local: {currentLocalIndex}, Desafio: {currentDesafioIndex}");
            StartCoroutine(TransitionToLocal(0, false));
            return;
        }
        
        // Atualiza o estado
        desafioAtual = locais[currentLocalIndex].desafios[currentDesafioIndex];

        if (desafioAtual.panoramaMaterial == null)
        {
            Debug.LogError($"Material nulo para o desafio {currentDesafioIndex} do local {locais[currentLocalIndex].locationName}. Verifique o caminho no JSON e se o asset existe em Resources.");
        }
        
        // Atualiza os objetos da cena
        panoramaSphereRenderer.transform.rotation = Quaternion.Euler(0, desafioAtual.initialYRotation, 0);
        panoramaSphereRenderer.material = desafioAtual.panoramaMaterial;
        
        // Manda o UIManager atualizar a UI
        uiManager.ApresentarDesafio(desafioAtual);
        
        // Libera a trava
        isAnswering = false;
    }

    /// <summary>
    /// Chamado pelo evento 'OnAnswerButtonClicked' do UIManager.
    /// Esta é a LÓGICA do quiz.
    /// </summary>
    public void CheckAnswer(int selectedIndex)
    {
        if (isAnswering) return; 
        isAnswering = true; // Ativa a trava

        // Manda o UIManager desativar os botões
        uiManager.SetAllButtonsInteractable(false, desafioAtual.answers.Count);

        // A lógica de verificação
        if (selectedIndex == desafioAtual.correctAnswerIndex)
        {
            StartCoroutine(HandleCorrectAnswer(selectedIndex));
        }
        else
        {
            StartCoroutine(HandleIncorrectAnswer(selectedIndex));
        }
    }
    
    // --- CORROTINAS DE EFEITOS E TRANSIÇÕES ---

    /// <summary>
    /// Gerencia a transição completa entre locais.
    /// </summary>
    private IEnumerator TransitionToLocal(int localIndex, bool isFirstLoad = false) 
    {
        if (!isFirstLoad)
        {
            // 1. Escurece a tela
            yield return StartCoroutine(uiManager.FadeOut(fadeDuration));
            audioSource.Stop(); // Para a música
            
            if (locationVictorySound != null) 
                audioSource.PlayOneShot(locationVictorySound, sfxVolume);
                
            // 2. Pausa na tela preta (só se houver fade)
            if (uiManager.HasFadeScreen)
            {
                yield return new WaitForSeconds(waitOnBlackScreenDelay);
            }
        }

        // 3. Carrega os dados do novo local (isso é rápido)
        CarregarDadosDoLocal(localIndex);

        // 4. Clareia a tela
        yield return StartCoroutine(uiManager.FadeIn(fadeDuration));
    }

    /// <summary>
    /// Lógica para quando o usuário acerta a resposta.
    /// </summary>
    private IEnumerator HandleCorrectAnswer(int correctButtonIndex)
    {
        // Feedback visual (via UIManager)
        uiManager.SetButtonFeedback(correctButtonIndex, uiManager.correctColor);
        // Feedback sonoro (via AudioSource local)
        if(correctAnswerSound != null) audioSource.PlayOneShot(correctAnswerSound, sfxVolume); 
        
        yield return new WaitForSeconds(feedbackDelay);

        // Avança para o próximo desafio
        currentDesafioIndex++;
        
        // Verifica se completou todos os desafios do local atual.
        if (currentDesafioIndex >= locais[currentLocalIndex].desafios.Count)
        {
            // Verifica se este era o último local da lista.
            if (currentLocalIndex >= locais.Count - 1)
            {
                // SIM, era o último. Retorna ao Lobby.
                yield return StartCoroutine(ReturnToLobby());
            }
            else
            {
                // NÃO, ainda há locais. Vai para o próximo.
                int proximoLocalIndex = currentLocalIndex + 1;
                yield return StartCoroutine(TransitionToLocal(proximoLocalIndex, false)); 
            }
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
    private IEnumerator HandleIncorrectAnswer(int incorrectButtonIndex)
    {
        // Feedback visual e sonoro
        uiManager.SetButtonFeedback(incorrectButtonIndex, uiManager.incorrectColor);
        if(incorrectAnswerSound != null) audioSource.PlayOneShot(incorrectAnswerSound, sfxVolume);
        
        yield return new WaitForSeconds(feedbackDelay);
        
        // Manda o UIManager resetar os botões para a nova tentativa
        uiManager.ResetButtonsToNormal(desafioAtual.answers.Count);
        
        // Libera a trava
        isAnswering = false;
    }
    
    /// <summary>
    /// Executa o fade out final e carrega a cena do Lobby.
    /// </summary>
    private IEnumerator ReturnToLobby()
    {
        // 1. Escurece a tela
        yield return StartCoroutine(uiManager.FadeOut(fadeDuration));
        audioSource.Stop();
        
        if (locationVictorySound != null) 
            audioSource.PlayOneShot(locationVictorySound, sfxVolume);
            
        // 2. Espera na tela preta (só se houver fade)
        if (uiManager.HasFadeScreen)
        {
            yield return new WaitForSeconds(waitOnBlackScreenDelay);
        }

        // 3. Carrega a cena do Lobby
        if (string.IsNullOrEmpty(lobbySceneName))
        {
            Debug.LogError("O 'lobbySceneName' não foi definido no TourManager!");
            yield break;
        }
        
        SceneManager.LoadScene(lobbySceneName);
    }
}