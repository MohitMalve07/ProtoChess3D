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
    private Material[] originalMaterials = new Material[64]; // Array to store original materials for reverting
    private GameObject lastHighlightedSquare = null;

    void Awake()
    {
        GenerateBoard();
    }

    /// <summary>
    /// Generates the 8x8 chessboard grid using the specified prefab.
    /// </summary>
    private void GenerateBoard()
    {
        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                // Calculate the position for the current square
                Vector3 position = new Vector3(col * (squareSize + squareSpacing), 0, row * (squareSize + squareSpacing));

                // Instantiate the square prefab and store it in the array
                GameObject square = Instantiate(squarePrefab, position, Quaternion.identity, this.transform);

                // Set the name of the tile for easy debugging
                square.name = $"X:{col},Y:{row}";

                squares[row, col] = square;
                originalMaterials[row * 8 + col] = square.GetComponent<Renderer>().material;
            }
        }
    }

    /// <summary>
    /// Returns the world position of the square at the given row and column.
    /// </summary>
    /// <param name="row">The row index (0-7).</param>
    /// <param name="col">The column index (0-7).</param>
    /// <returns>The Vector3 world position of the square.</returns>
    public Vector3 GetSquarePosition(int row, int col)
    {
        if (row < 0 || row >= 8 || col < 0 || col >= 8)
        {
            Debug.LogError($"Invalid board coordinates: row={row}, col={col}");
            return Vector3.zero;
        }

        // Return the position of the square, adjusted to be centered on the origin
        Vector3 squarePosition = squares[row, col].transform.position;
        return squarePosition;
    }

    void Update()
    {
        HandleHighlight();
    }

    private void HandleHighlight()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            GameObject hitSquare = hit.collider.gameObject;

            // Check if the raycast hit a square on the board
            if (hitSquare.GetComponent<Renderer>() != null)
            {
                // If we've hit a new square, and it's not the one we're already highlighting
                if (lastHighlightedSquare != hitSquare)
                {
                    // Revert the last highlighted square's material if it exists
                    if (lastHighlightedSquare != null)
                    {
                        int index = GetSquareIndex(lastHighlightedSquare.name);
                        lastHighlightedSquare.GetComponent<Renderer>().material = originalMaterials[index];
                    }

                    // Apply the highlight material to the new square
                    hitSquare.GetComponent<Renderer>().material = highlightMaterial;
                    lastHighlightedSquare = hitSquare;
                }
            }
        }
        else
        {
            // If the raycast is no longer hitting anything, revert the last highlighted square
            if (lastHighlightedSquare != null)
            {
                int index = GetSquareIndex(lastHighlightedSquare.name);
                lastHighlightedSquare.GetComponent<Renderer>().material = originalMaterials[index];
                lastHighlightedSquare = null;
            }
        }
    }

    /// <summary>
    /// Gets the index of the square from its name.
    /// This is a helper method to find the correct index in the originalMaterials array.
    /// </summary>
    private int GetSquareIndex(string squareName)
    {
        string[] parts = squareName.Split(',');
        int col = int.Parse(parts[0].Replace("X:", ""));
        int row = int.Parse(parts[1].Replace("Y:", ""));
        return row * 8 + col;
    }
}
