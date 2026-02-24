using UnityEngine;
using UnityEngine.UI;

namespace ToioLabs.UI
{
    /// <summary>
    /// "Connect the Dots" calibration visual feedback.
    /// After 4 points are recorded, dots/edges fade out and the filled
    /// rectangle remains as the live touch panel (handed off via MakePanelLive).
    /// A TouchInputReceiver is automatically added to the filled rect.
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

        [SerializeField, Tooltip("Duration of pulse ring expansion (seconds).")]
        private float _pulseDuration = 0.5f;

        [SerializeField, Tooltip("Maximum scale of the pulse ring.")]
        private float _pulseMaxScale = 3f;

        [SerializeField, Tooltip("Duration of edge slide-in animation (seconds).")]
        private float _edgeAnimDuration = 0.25f;

        [SerializeField, Tooltip("Duration of filled rect fade-in (seconds).")]
        private float _rectFadeDuration = 0.4f;

        [SerializeField, Tooltip("Duration of decoration fade-out (seconds).")]
        private float _fadeDecorationsDuration = 0.4f;

        // Runtime visual elements
        private readonly RectTransform[] _dots = new RectTransform[4];
        private readonly CanvasGroup[] _dotGroups = new CanvasGroup[4];
        private readonly RectTransform[] _pulseRings = new RectTransform[4];
        private readonly CanvasGroup[] _pulseGroups = new CanvasGroup[4];
        private readonly RectTransform[] _edges = new RectTransform[4];
        private readonly Image[] _edgeImages = new Image[4];
        private RectTransform _filledRect;
        private QuadrilateralGraphic _filledRectGraphic;
        private CanvasGroup _overlayGroup;
        private readonly Vector2[] _dotPositions = new Vector2[4];
        private int _dotCount;

        // Pulse animation (single at a time)
        private bool _animatingPulse;
        private int _pulseIndex;
        private float _pulseStartTime;

        // Edge animation queue — supports sequential playback
        private struct EdgeAnimRequest
        {
            public int EdgeIndex;
            public Vector2 From;
            public Vector2 To;
        }
        private readonly System.Collections.Generic.Queue<EdgeAnimRequest> _edgeQueue =
            new System.Collections.Generic.Queue<EdgeAnimRequest>(4);
        private bool _animatingEdge;
        private int _currentEdgeIndex;
        private Vector2 _edgeFrom;
        private Vector2 _edgeTo;
        private float _edgeStartTime;

        // Filled rect fade
        private bool _animatingRectFade;
        private float _rectFadeStartTime;

        // Decoration fade-out
        private bool _animatingFadeDecorations;
        private float _fadeDecorationsStartTime;

        // Callback after MakePanelLive completes
        private System.Action<RectTransform> _onPanelReady;

        // Cached reference to the TouchInputReceiver added to FilledRect
        private TouchInputReceiver _inputReceiver;

        /// <summary>The RectTransform of the filled calibration rect (valid after ShowFilledRect).</summary>
        public RectTransform FilledRect => _filledRect;

        private void Awake()
        {
            if (_parentRect == null)
                _parentRect = GetComponent<RectTransform>();

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

        /// <summary>Show a dot at the given panel-local position with a pulse animation.</summary>
        public void ShowDot(int index, Vector2 panelLocalPos)
        {
            if (index < 0 || index >= 4) return;

            _dotPositions[index] = panelLocalPos;
            _dotCount = Mathf.Max(_dotCount, index + 1);

            if (_dots[index] == null)
                CreateDot(index);

            _dots[index].anchoredPosition = panelLocalPos;
            _dots[index].gameObject.SetActive(true);

            if (_dotGroups[index] != null)
                _dotGroups[index].alpha = 1f;

            StartPulse(index);
        }

        /// <summary>
        /// Enqueue an edge animation between two dot positions.
        /// Multiple edges are played sequentially (no overwrite).
        /// </summary>
        public void ShowEdge(int edgeIndex, int fromDotIndex, int toDotIndex)
        {
            if (edgeIndex < 0 || edgeIndex >= 4) return;
            if (fromDotIndex < 0 || fromDotIndex >= _dotCount) return;
            if (toDotIndex < 0 || toDotIndex >= _dotCount) return;

            if (_edges[edgeIndex] == null)
                CreateEdge(edgeIndex);

            // Position at start point with zero length
            PositionEdge(edgeIndex, _dotPositions[fromDotIndex], _dotPositions[fromDotIndex]);
            _edges[edgeIndex].gameObject.SetActive(true);

            var request = new EdgeAnimRequest
            {
                EdgeIndex = edgeIndex,
                From = _dotPositions[fromDotIndex],
                To = _dotPositions[toDotIndex]
            };

            if (!_animatingEdge)
            {
                // Start immediately
                StartEdgeAnim(request);
            }
            else
            {
                // Queue for later
                _edgeQueue.Enqueue(request);
            }
        }

        /// <summary>Show a semi-transparent filled rectangle across all 4 calibration points.</summary>
        public void ShowFilledRect()
        {
            if (_dotCount < 4) return;

            if (_filledRect == null)
                CreateFilledRect();

            // Set actual 4 corners
            _filledRectGraphic.SetVertices(_dotPositions[0], _dotPositions[1], _dotPositions[2], _dotPositions[3]);
            _filledRect.gameObject.SetActive(true);

            _filledRectGraphic.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0f);
            _animatingRectFade = true;
            _rectFadeStartTime = Time.time;
        }

        /// <summary>
        /// Fades out dots and edges, keeps the filled rect visible.
        /// Adds a TouchInputReceiver to the filled rect.
        /// Calls onReady(filledRectRT) when ready.
        /// </summary>
        public void MakePanelLive(System.Action<RectTransform> onReady)
        {
            if (_filledRect == null)
            {
                onReady?.Invoke(null);
                return;
            }

            // Ensure the rect is fully visible
            if (_filledRectGraphic != null)
                _filledRectGraphic.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.18f);

            _onPanelReady = onReady;
            _animatingFadeDecorations = true;
            _fadeDecorationsStartTime = Time.time;
        }

        /// <summary>Immediately remove all visual elements and reset state.</summary>
        public void Reset()
        {
            _animatingPulse = false;
            _animatingEdge = false;
            _animatingRectFade = false;
            _animatingFadeDecorations = false;
            _edgeQueue.Clear();
            _onPanelReady = null;
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

            // Destroy TouchInputReceiver before destroying FilledRect
            if (_inputReceiver != null)
            {
                Destroy(_inputReceiver);
                _inputReceiver = null;
            }

            if (_filledRect != null)
            {
                Destroy(_filledRect.gameObject);
                _filledRect = null;
                _filledRectGraphic = null;
            }

            if (_overlayGroup != null)
            {
                _overlayGroup.alpha = 1f;
                _overlayGroup.blocksRaycasts = false;
                _overlayGroup.interactable = false;
            }
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
            img.raycastTarget = false;

            var cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;

            _dots[index] = rt;
            _dotGroups[index] = cg;

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
            rt.pivot = new Vector2(0f, 0.5f);

            var img = go.GetComponent<Image>();
            img.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.6f);
            img.raycastTarget = false;

            _edges[index] = rt;
            _edgeImages[index] = img;

            go.SetActive(false);
        }

        private void CreateFilledRect()
        {
            var go = new GameObject("CalibFilledRect", typeof(RectTransform), typeof(CanvasRenderer), typeof(QuadrilateralGraphic));
            go.transform.SetParent(_parentRect, false);
            // Place as last sibling so raycasts hit it first
            go.transform.SetAsLastSibling();

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            var graphic = go.GetComponent<QuadrilateralGraphic>();
            graphic.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0f);
            graphic.raycastTarget = false; // enabled later in MakePanelLive

            _filledRect = rt;
            _filledRectGraphic = graphic;

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

        private void StartEdgeAnim(EdgeAnimRequest req)
        {
            _currentEdgeIndex = req.EdgeIndex;
            _edgeFrom = req.From;
            _edgeTo = req.To;
            _edgeStartTime = Time.time;
            _animatingEdge = true;
        }

        private void UpdateEdgeAnimation()
        {
            float elapsed = Time.time - _edgeStartTime;
            float t = Mathf.Clamp01(elapsed / _edgeAnimDuration);
            float eased = 1f - (1f - t) * (1f - t);

            Vector2 currentEnd = Vector2.Lerp(_edgeFrom, _edgeTo, eased);
            PositionEdge(_currentEdgeIndex, _edgeFrom, currentEnd);

            if (t >= 1f)
            {
                // Process next in queue
                if (_edgeQueue.Count > 0)
                {
                    StartEdgeAnim(_edgeQueue.Dequeue());
                }
                else
                {
                    _animatingEdge = false;
                }
            }
        }

        private void UpdateRectFadeAnimation()
        {
            float elapsed = Time.time - _rectFadeStartTime;
            float t = Mathf.Clamp01(elapsed / _rectFadeDuration);

            float alpha = Mathf.Lerp(0f, 0.1f, t);
            if (_filledRectGraphic != null)
                _filledRectGraphic.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b, alpha);

            if (t >= 1f)
                _animatingRectFade = false;
        }

        private void UpdateFadeDecorationsAnimation()
        {
            float elapsed = Time.time - _fadeDecorationsStartTime;
            float t = Mathf.Clamp01(elapsed / _fadeDecorationsDuration);
            float alpha = Mathf.Lerp(1f, 0f, t);

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

                for (int i = 0; i < 4; i++)
                {
                    DestroyChild(ref _dots[i]);
                    _dotGroups[i] = null;
                    DestroyChild(ref _pulseRings[i]);
                    _pulseGroups[i] = null;
                    DestroyChild(ref _edges[i]);
                    _edgeImages[i] = null;
                }

                // Enable raycasts on overlay so FilledRect receives clicks
                if (_overlayGroup != null)
                {
                    _overlayGroup.interactable = true;
                    _overlayGroup.blocksRaycasts = true;
                }

                // Add TouchInputReceiver to FilledRect
                if (_filledRect != null && _inputReceiver == null)
                {
                    _inputReceiver = _filledRect.gameObject.AddComponent<TouchInputReceiver>();
                    _filledRectGraphic.raycastTarget = true;
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

    public class QuadrilateralGraphic : MaskableGraphic, ICanvasRaycastFilter
    {
        private Vector2[] _verts = new Vector2[4];

        public void SetVertices(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            _verts[0] = p0;
            _verts[1] = p1;
            _verts[2] = p2;
            _verts[3] = p3;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            UIVertex vert = UIVertex.simpleVert;
            vert.color = color;

            for (int i = 0; i < 4; i++)
            {
                vert.position = _verts[i];
                vh.AddVert(vert);
            }

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, sp, eventCamera, out Vector2 localPoint))
                return false;

            bool inside = false;
            for (int i = 0, j = 3; i < 4; j = i++)
            {
                if (((_verts[i].y > localPoint.y) != (_verts[j].y > localPoint.y)) &&
                    (localPoint.x < (_verts[j].x - _verts[i].x) * (localPoint.y - _verts[i].y) / (_verts[j].y - _verts[i].y) + _verts[i].x))
                {
                    inside = !inside;
                }
            }
            return inside;
        }
    }
}
