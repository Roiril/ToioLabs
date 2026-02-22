using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ToioLabs.UI
{
    /// <summary>
    /// Thin component attached to the calibration FilledRect.
    /// Receives pointer events and forwards screen-space positions
    /// to subscribers via the OnInput event.
    /// Requires an Image with raycastTarget=true on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class TouchInputReceiver : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        /// <summary>Fired on pointer-down and drag with screen-space position.</summary>
        public event System.Action<Vector2> OnInput;

        public void OnPointerDown(PointerEventData eventData)
        {
            OnInput?.Invoke(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            OnInput?.Invoke(eventData.position);
        }
    }
}
