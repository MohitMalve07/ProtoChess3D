using UnityEngine;

public class CameraToggle : MonoBehaviour
{
    // Assign your main camera here in the Inspector.
    public Camera mainCamera;

    // --- Standard Angled View Values ---
    private Vector3 whiteSidePosition_Standard = new Vector3(3.5f, 8f, -2f);
    private Quaternion whiteSideRotation_Standard = Quaternion.Euler(60f, 0f, 0f);
    private Vector3 blackSidePosition_Standard = new Vector3(3.5f, 8f, 9f);
    private Quaternion blackSideRotation_Standard = Quaternion.Euler(60f, -180f, 0f);

    // --- Tactical Top-Down View Values ---
    private Vector3 whiteSidePosition_Tactical = new Vector3(3.5f, 10f, 0.5f);
    private Quaternion whiteSideRotation_Tactical = Quaternion.Euler(75f, 0f, 0f);
    private Vector3 blackSidePosition_Tactical = new Vector3(3.5f, 10f, 6.5f);
    private Quaternion blackSideRotation_Tactical = Quaternion.Euler(75f, -180f, 0f);

    // --- Main Menu Camera ---
    [Header("Main Menu Camera")]
    [SerializeField] private bool startInMainMenu = true;
    [SerializeField] private Vector3 mainMenuPosition = new Vector3(5.8f, 6.2f, -3.5f);
    [SerializeField] private Vector3 mainMenuEulerRotation = new Vector3(43f, -40.5f, 0f);

    // --- State Variables ---
    private bool isMainMenu = true;
    private bool isBlackSide = false;
    private bool isTacticalView = false; // Flag to track the current view mode
    private float smoothness = 5.0f;
    private bool lockCamera = false; // --- NEW: Prevents rotation in PvAI ---

    void Awake()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        isMainMenu = startInMainMenu;
        if (isMainMenu && mainCamera != null)
        {
            mainCamera.transform.SetPositionAndRotation(mainMenuPosition, Quaternion.Euler(mainMenuEulerRotation));
        }
    }

    /// <summary>
    /// Transitions the camera from the Main Menu showcase angle into the gameplay angle.
    /// </summary>
    public void ExitMainMenu()
    {
        isMainMenu = false;
        isBlackSide = false;
        isTacticalView = false;
    }

    /// <summary>
    /// Configures the camera behavior based on the game mode.
    /// Called by GameManager when the game starts.
    /// </summary>
    public void ConfigureForSinglePlayer(bool isSinglePlayer)
    {
        lockCamera = isSinglePlayer;
        if (isSinglePlayer)
        {
            isBlackSide = false; // Ensure we are looking from White's perspective
        }
    }

    /// <summary>
    /// Toggles the camera to the next player's side.
    /// </summary>
    public void ToggleView()
    {
        // --- MODIFIED: Don't toggle if camera is locked (PvAI mode) ---
        if (lockCamera) return;

        isBlackSide = !isBlackSide;
    }

    void Update()
    {
        if (mainCamera == null) return;

        // Check for the "T" key press to toggle the view mode
        if (!isMainMenu && Input.GetKeyDown(KeyCode.T))
        {
            isTacticalView = !isTacticalView;
        }

        // Determine the target position and rotation based on the current state
        Vector3 targetPosition;
        Quaternion targetRotation;

        if (isMainMenu)
        {
            targetPosition = mainMenuPosition;
            targetRotation = Quaternion.Euler(mainMenuEulerRotation);
        }
        else if (isBlackSide)
        {
            // Set target for Black's turn
            targetPosition = isTacticalView ? blackSidePosition_Tactical : blackSidePosition_Standard;
            targetRotation = isTacticalView ? blackSideRotation_Tactical : blackSideRotation_Standard;
        }
        else
        {
            // Set target for White's turn
            targetPosition = isTacticalView ? whiteSidePosition_Tactical : whiteSidePosition_Standard;
            targetRotation = isTacticalView ? whiteSideRotation_Tactical : whiteSideRotation_Standard;
        }

        // Use a single Lerp/Slerp to smoothly transition to the target view
        mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, targetPosition, smoothness * Time.deltaTime);
        mainCamera.transform.rotation = Quaternion.Slerp(mainCamera.transform.rotation, targetRotation, smoothness * Time.deltaTime);
    }
}