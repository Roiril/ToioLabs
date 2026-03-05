using UnityEngine;
using UnityEngine.UI;

namespace ToioLabs.UI
{
    /// <summary>
    /// Wires a UI Button to ToioUIController.StartRecalibration().
    /// Attach to a GameObject that has a Button component.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class RecalibrateButton : MonoBehaviour
    {
        [Header("References")]
        [SerializeField, Tooltip("The ToioUIController to recalibrate.")]
        private ToioUIController _controller;

        private Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            _button.onClick.AddListener(HandleClick);
        }

        private void OnDisable()
        {
            _button.onClick.RemoveListener(HandleClick);
        }

        private void HandleClick()
        {
            if (_controller != null)
                _controller.StartRecalibration();
        }
    }
}
