using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace ToioLabs.UI
{
    /// <summary>
    /// "Connect the Dots" calibration visual feedback.
    /// After completion, dots and edges fade out while the filled rectangle
    /// remains as the new touch input area (handed off via MakePanelLive).
    /// </summary>
    public class CalibrationVisualizer : MonoBehaviour
    {
        [Header("Visual Settings")]
        [SerializeField, Tooltip("Parent RectTransform to place visual elements in.")]
        private RectTransform _parentRect;

        [SerializeField, Tooltip("Color of dots and edges.")]
        private Color _accentColor = new Color(0f, 0.831f, 1f, 1f); // #00D4FF

        [SerializeField, Tooltip("Diameter of each calibration dot.")]
        private float _dotSize = 16f;

        [SerializeField, Tooltip("Thickness of the edge lines.")]
        private float _edgeThickness = 2f;

        [SerializeField, Tooltip("Duration of dot fade-in (seconds).")]
        private float _dotFadeInDuration = 0.3f;

        [SerializeField, Tooltip("Duration of pulse ring expansion (seconds).")]
        private float _pulseDuration = 0.5f;

        [SerializeField, Tooltip("Maximum scale of the pulse ring.")]
        private float _pulseMaxScale = 3f;

        [SerializeField, Tooltip("Duration of edge slide-in animation (seconds).")]
        private float _edgeAnimDuration = 0.25f;

        [SerializeField, Tooltip("Duration of filled rect fade-in (seconds).")]
        private float _rectFadeDuration = 0.4f;

        [SerializeField, Tooltip("Duration of transition out (seconds).")]
        private float _transitionOutDuration = 0.5f;

        // Runtime state
        private readonly RectTransform[] _dots = new RectTransform[4];
        private readonly CanvasGroup[] _dotGroups = new CanvasGroup[4];
        private readonly RectTransform[] _pulseRings = new RectTransform[4];
        private readonly CanvasGroup[] _pulseGroups = new CanvasGroup[4];
        private readonly RectTransform[] _edges = new RectTransform[4]; // FL-FR, FR-BR, BR-BL, BL-FL
        private readonly Image[] _edgeImages = new Image[4];
        private RectTransform _filledRect;
        private Image _filledRectImage;
        private CanvasGroup _overlayGroup;
        private readonly Vector2[] _dotPositions = new Vector2[4];
        private int _dotCount;

        // Animation tracking (no coroutines to avoid GC)
        private bool _animatingPulse;
        private int _pulseIndex;
        private float _pulseStartTime;

        private bool _animatingEdge;
        private int _edgeIndex;
        private float _edgeStartTime;
        private Vector2 _edgeFrom;
        private Vector2 _edgeTo;

        private bool _animatingRectFade;
        private float _rectFadeStartTime;

        private bool _animatingFadeDecorations;
        private float _fadeDecorationsStartTime;
        private float _fadeDecorationsDuration = 0.4f;

        // Callback after MakePanelLive completes — passes FilledRect RT
        private System.Action<RectTransform> _onPanelReady;

        /// <summary>The RectTransform of the filled calibration rect (valid after ShowFilledRect).</summary>
        public RectTransform FilledRect => _filledRect;

        private void Awake()
        {
            if (_parentRect == null)
                _parentRect = GetComponent<RectTransform>();

            // Create an overlay CanvasGroup for the whole visualizer
            _overlayGroup = gameObject.GetComponent<CanvasGroup>();
            if (_overlayGroup == null)
                _overlayGroup = gameObject.AddComponent<CanvasGroup>();
            _overlayGroup.alpha = 1f;
            _overlayGroup.blocksRaycasts = false;
            _overlayGroup.interactable = false;
        }

        private void Update()
        {
            if (_animatingPulse) UpdatePulseAnimation();
            if (_animatingEdge) UpdateEdgeAnimation();
            if (_animatingRectFade) UpdateRectFadeAnimation();
            if (_animatingFadeDecorations) UpdateFadeDecorationsAnimation();
        }

        // ─────── Public API ───────

        /// <summary>
        /// Show a dot at the given panel-local position with a pulse animation.
        /// </summary>
        public void ShowDot(int index, Vector2 panelLocalPos)
        {
            if (index < 0 || index >= 4) return;

            _dotPositions[index] = panelLocalPos;
            _dotCount = Mathf.Max(_dotCount, index + 1);

            // Create dot
            if (_dots[index] == null)
                CreateDot(index);

            _dots[index].anchoredPosition = panelLocalPos;
            _dots[index].gameObject.SetActive(true);

            // Fade in dot
            if (_dotGroups[index] != null)
                _dotGroups[index].alpha = 1f;

            // Start pulse
            StartPulse(index);
        }

        /// <summary>
        /// Show an edge line between two recorded dot positions.
        /// Edge indices: 0=FL-FR, 1=FR-BR, 2=BR-BL, 3=BL-FL
        /// </summary>
        public void ShowEdge(int edgeIndex, int fromDotIndex, int toDotIndex)
        {
            if (edgeIndex < 0 || edgeIndex >= 4) return;
            if (fromDotIndex < 0 || fromDotIndex >= _dotCount) return;
            if (toDotIndex < 0 || toDotIndex >= _dotCount) return;

            if (_edges[edgeIndex] == null)
                CreateEdge(edgeIndex);

            _edgeFrom = _dotPositions[fromDotIndex];
            _edgeTo = _dotPositions[toDotIndex];

            // Position edge at 'from', will animate to full length
            PositionEdge(edgeIndex, _edgeFrom, _edgeFrom);
            _edges[edgeIndex].gameObject.SetActive(true);

            _animatingEdge = true;
            _edgeIndex = edgeIndex;
            _edgeStartTime = Time.time;
        }

        /// <summary>
        /// Show a semi-transparent filled rectangle across all 4 calibration points.
        /// </summary>
        public void ShowFilledRect()
        {
            if (_dotCount < 4) return;

            if (_filledRect == null)
                CreateFilledRect();

            // Calculate bounding rect from the 4 dot positions
            float minX = Mathf.Min(_dotPositions[0].x, Mathf.Min(_dotPositions[1].x, Mathf.Min(_dotPositions[2].x, _dotPositions[3].x)));
            float maxX = Mathf.Max(_dotPositions[0].x, Mathf.Max(_dotPositions[1].x, Mathf.Max(_dotPositions[2].x, _dotPositions[3].x)));
            float minY = Mathf.Min(_dotPositions[0].y, Mathf.Min(_dotPositions[1].y, Mathf.Min(_dotPositions[2].y, _dotPositions[3].y)));
            float maxY = Mathf.Max(_dotPositions[0].y, Mathf.Max(_dotPositions[1].y, Mathf.Max(_dotPositions[2].y, _dotPositions[3].y)));

            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            _filledRect.anchoredPosition = new Vector2(cx, cy);
            _filledRect.sizeDelta = new Vector2(maxX - minX, maxY - minY);
            _filledRect.gameObject.SetActive(true);

            // Animate alpha
            _filledRectImage.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0f);
            _animatingRectFade = true;
            _rectFadeStartTime = Time.time;
        }

        /// <summary>
        /// Fades out dots and edges, keeps the filled rect visible.
        /// Calls onReady(filledRectRT) when the fade is done.
        /// </summary>
        public void MakePanelLive(System.Action<RectTransform> onReady)
        {
            if (_filledRect == null)
            {
                onReady?.Invoke(null);
                return;
            }
            // Ensure the rect is fully visible
            if (_filledRectImage != null)
                _filledRectImage.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.18f);

            _onPanelReady = onReady;
            _animatingFadeDecorations = true;
            _fadeDecorationsStartTime = Time.time;
        }

        /// <summary>
        /// Immediately remove all visual elements and reset state.
        /// </summary>
        public void Reset()
        {
            _animatingPulse = false;
            _animatingEdge = false;
            _animatingRectFade = false;
            _animatingFadeDecorations = false;
            _dotCount = 0;

            for (int i = 0; i < 4; i++)
            {
                DestroyChild(ref _dots[i]);
                _dotGroups[i] = null;
                DestroyChild(ref _pulseRings[i]);
                _pulseGroups[i] = null;
                DestroyChild(ref _edges[i]);
                _edgeImages[i] = null;
            }

            if (_filledRect != null)
            {
                Destroy(_filledRect.gameObject);
                _filledRect = null;
                _filledRectImage = null;
            }

            if (_overlayGroup != null)
                _overlayGroup.alpha = 1f;
        }

        // ─────── Creation Helpers ───────

        private void CreateDot(int index)
        {
            var go = new GameObject($"CalibDot_{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(_parentRect, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(_dotSize, _dotSize);

            var img = go.GetComponent<Image>();
            img.color = _accentColor;
            // Make it a circle by using a round sprite if available, otherwise square is fine
            img.raycastTarget = false;

            var cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;

            _dots[index] = rt;
            _dotGroups[index] = cg;

            // Create pulse ring as a child
            CreatePulseRing(index, rt);
        }

        private void CreatePulseRing(int index, RectTransform parent)
        {
            var go = new GameObject($"PulseRing_{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(_dotSize, _dotSize);
            rt.localScale = Vector3.one;

            var img = go.GetComponent<Image>();
            img.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.5f);
            img.raycastTarget = false;

            var cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;

            _pulseRings[index] = rt;
            _pulseGroups[index] = cg;

            go.SetActive(false);
        }

        private void CreateEdge(int index)
        {
            var go = new GameObject($"CalibEdge_{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_parentRect, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f); // pivot at left-center for stretch animation

            var img = go.GetComponent<Image>();
            img.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.6f);
            img.raycastTarget = false;

            _edges[index] = rt;
            _edgeImages[index] = img;

            go.SetActive(false);
        }

        private void CreateFilledRect()
        {
            var go = new GameObject("CalibFilledRect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_parentRect, false);
            // Place behind dots (first sibling)
            go.transform.SetAsFirstSibling();

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var img = go.GetComponent<Image>();
            img.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0f);
            img.raycastTarget = false;

            _filledRect = rt;
            _filledRectImage = img;

            go.SetActive(false);
        }

        // ─────── Animation Updates (GC-free) ───────

        private void StartPulse(int index)
        {
            if (_pulseRings[index] == null) return;
            _pulseRings[index].gameObject.SetActive(true);
            _pulseRings[index].localScale = Vector3.one;
            _pulseGroups[index].alpha = 0.6f;

            _animatingPulse = true;
            _pulseIndex = index;
            _pulseStartTime = Time.time;
        }

        private void UpdatePulseAnimation()
        {
            float elapsed = Time.time - _pulseStartTime;
            float t = Mathf.Clamp01(elapsed / _pulseDuration);

            // Ease out
            float eased = 1f - (1f - t) * (1f - t);

            float scale = Mathf.Lerp(1f, _pulseMaxScale, eased);
            float alpha = Mathf.Lerp(0.6f, 0f, eased);

            if (_pulseRings[_pulseIndex] != null)
            {
                _pulseRings[_pulseIndex].localScale = new Vector3(scale, scale, 1f);
                _pulseGroups[_pulseIndex].alpha = alpha;
            }

            if (t >= 1f)
            {
                _animatingPulse = false;
                if (_pulseRings[_pulseIndex] != null)
                    _pulseRings[_pulseIndex].gameObject.SetActive(false);
            }
        }

        private void UpdateEdgeAnimation()
        {
            float elapsed = Time.time - _edgeStartTime;
            float t = Mathf.Clamp01(elapsed / _edgeAnimDuration);

            // Ease out quad
            float eased = 1f - (1f - t) * (1f - t);

            Vector2 currentEnd = Vector2.Lerp(_edgeFrom, _edgeTo, eased);
            PositionEdge(_edgeIndex, _edgeFrom, currentEnd);

            if (t >= 1f)
            {
                _animatingEdge = false;
            }
        }

        private void UpdateRectFadeAnimation()
        {
            float elapsed = Time.time - _rectFadeStartTime;
            float t = Mathf.Clamp01(elapsed / _rectFadeDuration);

            // Target alpha: 0.1 (subtle fill)
            float alpha = Mathf.Lerp(0f, 0.1f, t);
            if (_filledRectImage != null)
                _filledRectImage.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b, alpha);

            if (t >= 1f)
            {
                _animatingRectFade = false;
            }
        }

        private void UpdateFadeDecorationsAnimation()
        {
            float elapsed = Time.time - _fadeDecorationsStartTime;
            float t = Mathf.Clamp01(elapsed / _fadeDecorationsDuration);
            float alpha = Mathf.Lerp(1f, 0f, t);

            // Fade dots and edges only
            for (int i = 0; i < 4; i++)
            {
                if (_dotGroups[i] != null) _dotGroups[i].alpha = alpha;
                if (_edgeImages[i] != null)
                {
                    Color c = _edgeImages[i].color;
                    _edgeImages[i].color = new Color(c.r, c.g, c.b, alpha * 0.6f);
                }
            }

            if (t >= 1f)
            {
                _animatingFadeDecorations = false;
                // Destroy dots and edges, keep filled rect
                for (int i = 0; i < 4; i++)
                {
                    DestroyChild(ref _dots[i]);
                    _dotGroups[i] = null;
                    DestroyChild(ref _pulseRings[i]);
                    _pulseGroups[i] = null;
                    DestroyChild(ref _edges[i]);
                    _edgeImages[i] = null;
                }
                // Allow raycasts on FilledRect (CanvasGroup.interactable=false blocked them)
                if (_overlayGroup != null)
                {
                    _overlayGroup.interactable = true;
                    _overlayGroup.blocksRaycasts = true;
                }
                _onPanelReady?.Invoke(_filledRect);
                _onPanelReady = null;
            }
        }

        // ─────── Utility ───────

        private void PositionEdge(int edgeIndex, Vector2 from, Vector2 to)
        {
            if (_edges[edgeIndex] == null) return;

            Vector2 diff = to - from;
            float length = diff.magnitude;
            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

            _edges[edgeIndex].anchoredPosition = from;
            _edges[edgeIndex].sizeDelta = new Vector2(length, _edgeThickness);
            _edges[edgeIndex].localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void DestroyChild(ref RectTransform rt)
        {
            if (rt != null)
            {
                Destroy(rt.gameObject);
                rt = null;
            }
        }
    }
}
