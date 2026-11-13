using System.Collections.Generic;
using UnityEngine;

public class BoardLayout : MonoBehaviour
{
    // Public variables to customize the board from the Inspector
    public GameObject squarePrefab;
    public Material highlightMaterial;
    public float squareSize = 1f;
    public float squareSpacing = 0.05f;

    // A 2D array to hold references to all the squares (GameObjects) on the board
    private GameObject[,] squares = new GameObject[8, 8];
    private Renderer[,] squareRenderers = new Renderer[8, 8];
    private Material[] originalMaterials = new Material[64];
    private readonly Dictionary<GameObject, int> squareIndexMap = new Dictionary<GameObject, int>(64);
    private GameObject lastHighlightedSquare = null;
    private int tileLayer = -1;
    private int tileLayerMask = Physics.DefaultRaycastLayers;

    void Awake()
    {
        tileLayer = LayerMask.NameToLayer("Tile");
        if (tileLayer >= 0)
        {
            tileLayerMask = 1 << tileLayer;
        }
        GenerateBoard();
    }

    /// <summary>
    /// Generates the 8x8 chessboard grid using the specified prefab.
    /// </summary>
    private void GenerateBoard()
    {
        float step = squareSize + squareSpacing;
        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                Vector3 position = new Vector3(col * step, 0, row * step);
                GameObject square = Instantiate(squarePrefab, position, Quaternion.identity, this.transform);
                square.name = $"X:{col},Y:{row}";
                if (tileLayer >= 0)
                {
                    square.layer = tileLayer;
                }

                int index = row * 8 + col;
                Renderer rend = square.GetComponent<Renderer>();
                squares[row, col] = square;
                squareRenderers[row, col] = rend;
                originalMaterials[index] = rend != null ? rend.sharedMaterial : null;
                squareIndexMap[square] = index;
            }
        }
    }

    /// <summary>
    /// Returns the world position of the square at the given row and column.
    /// </summary>
    public Vector3 GetSquarePosition(int row, int col)
    {
        if (row < 0 || row >= 8 || col < 0 || col >= 8)
        {
            Debug.LogError($"Invalid board coordinates: row={row}, col={col}");
            return Vector3.zero;
        }

        return squares[row, col].transform.position;
    }

    /// <summary>
    /// Returns the world position for a piece or highlight centered over (row, col) at height y.
    /// </summary>
    public Vector3 GetSquareWorldPosition(int row, int col, float y)
    {
        float step = squareSize + squareSpacing;
        return new Vector3(col * step, y, row * step);
    }

    /// <summary>
    /// Converts a tile's world position into board (row, col) indices respecting squareSize and squareSpacing.
    /// </summary>
    public bool TryGetBoardCoordinates(Vector3 worldPos, out int row, out int col)
    {
        float step = squareSize + squareSpacing;
        if (step <= 0f)
        {
            row = -1;
            col = -1;
            return false;
        }

        col = Mathf.RoundToInt(worldPos.x / step);
        row = Mathf.RoundToInt(worldPos.z / step);
        return row >= 0 && row < 8 && col >= 0 && col < 8;
    }

    void Update()
    {
        HandleHighlight();
    }

    private void HandleHighlight()
    {
        if (Camera.main == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 100f, tileLayerMask))
        {
            GameObject hitSquare = hit.collider.gameObject;
            if (squareIndexMap.ContainsKey(hitSquare))
            {
                if (lastHighlightedSquare != hitSquare)
                {
                    RevertLastHighlight();
                    Renderer rend = hitSquare.GetComponent<Renderer>();
                    if (rend != null)
                    {
                        rend.sharedMaterial = highlightMaterial;
                    }
                    lastHighlightedSquare = hitSquare;
                }
                return;
            }
        }

        RevertLastHighlight();
    }

    private void RevertLastHighlight()
    {
        if (lastHighlightedSquare != null && squareIndexMap.TryGetValue(lastHighlightedSquare, out int index))
        {
            Renderer rend = lastHighlightedSquare.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = originalMaterials[index];
            }
            lastHighlightedSquare = null;
        }
    }
}
