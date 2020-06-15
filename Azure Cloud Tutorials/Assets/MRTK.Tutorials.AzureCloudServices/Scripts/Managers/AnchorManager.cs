using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.SpatialAnchors;
using Microsoft.Azure.SpatialAnchors.Unity;
using MRTK.Tutorials.AzureCloudPower;
using MRTK.Tutorials.AzureCloudServices.Scripts.Domain;
using MRTK.Tutorials.AzureCloudServices.Scripts.UX;
using UnityEngine;

namespace MRTK.Tutorials.AzureCloudServices.Scripts.Managers
{
    /// <summary>
    /// Access point for Azure Spatial Anchors features.
    /// </summary>
    [RequireComponent(typeof(SpatialAnchorManager))]
    public class AnchorManager : MonoBehaviour
    {
        public event EventHandler<string> OnCreateAnchorSucceeded;
        public event EventHandler OnCreateAnchorFailed;
        public event EventHandler OnFindAnchorSucceeded;
        public event EventHandler OnPlaceAnchorCanceled;
        
        [Header("Anchor Manager")]
        [SerializeField]
        private SpatialAnchorManager cloudManager;
        [Header("Controller")]
        [SerializeField]
        private AnchorPlacementController anchorPlacementController;
        [SerializeField]
        private AnchorCreationController anchorCreationController;
        [Header("UX")]
        [SerializeField]
        private AnchorPosition anchorPositionPrefab;
        [SerializeField]
        private GameObject objectCardPrefab;
        [SerializeField]
        private AnchorArrowGuide anchorArrowGuide;

        private Dictionary<string, AnchorPosition> activeAnchors = new Dictionary<string, AnchorPosition>();
        private CloudSpatialAnchor currentCloudAnchor;
        private AnchorLocateCriteria anchorLocateCriteria;
        private CloudSpatialAnchorWatcher currentWatcher;
        private TrackedObject currentTrackedObject;

        private void Start()
        {
            // Subscribe to Azure Spatial Anchor events
            cloudManager.AnchorLocated += HandleAnchorLocated;
            anchorPlacementController.OnIndicatorPlaced += HandleOnAnchorPlaced;
            anchorPlacementController.OnIndicatorCanceled += HandleOnAnchorPlacementCanceled;
            anchorArrowGuide.gameObject.SetActive(false);
        }

        public bool CheckIsAnchorActiveForTrackedObject(string spatialAnchorId)
        {
            return activeAnchors.ContainsKey(spatialAnchorId);
        }

        public void GuideToAnchor(string spatialAnchorId)
        {
            if (activeAnchors.ContainsKey(spatialAnchorId))
            {
                var anchor = activeAnchors[spatialAnchorId];
                anchorArrowGuide.SetTargetObject(anchor.transform);
            }
        }

        /// <summary>
        /// Enables 'AnchorCreationIndicator'.
        /// Called from 'ObjectCard' > 'Save Location' button when user is ready to save location.
        /// Called from 'SaveLocationDialog' > 'ButtonTwoA' button ("No" button) when user rejects the anchor preview position.
        /// Hooked up in Unity.
        /// </summary>
        public void StartPlacingAnchor(TrackedObject trackedObject)
        {
            if (anchorArrowGuide.gameObject.activeInHierarchy)
            {
                Debug.Log("Anchor creation is already active.");
                return;
            }

            currentTrackedObject = trackedObject;
            Debug.Log("Placing anchor position process started.");
            anchorPlacementController.gameObject.SetActive(true);
            anchorPlacementController.StartIndicator();
        }

        /// <summary>
        /// Starts Azure Spatial Anchors create anchor process.
        /// Called from 'SaveLocationDialog' > 'ButtonTwoA' button ("Yes" button) when user confirms an anchor should be created at the anchor preview position.
        /// Hooked up in Unity.
        /// </summary>
        public void CreateAnchor(Transform indicatorTransform)
        {
            Debug.Log("Anchor position has been set, saving location process started.");
            // currentAnchorPositionGo = Instantiate(anchorPositionPrefab, indicatorTransform.position, indicatorTransform.rotation);
            if (Application.isEditor)
            {
                CreateAsaAnchorEditor(indicatorTransform);
            }
            else
            {
                CreateAsaAnchor(indicatorTransform);
            }
        }

        // TODO: Update summary when known where to hook this up 
        /// <summary>
        /// Starts Azure Spatial Anchors find anchor process.
        /// Called from 'Not-sure-where' when user is ready to find location.
        /// <param name="anchorId">Azure Spatial Anchors anchor ID of the object to find.</param>
        /// </summary>
        public void FindAnchor(TrackedObject trackedObject)
        {
            currentTrackedObject = trackedObject;
            if (Application.isEditor)
            {
                FindAsaAnchorEditor();
            }
            else
            {
                FindAsaAnchor();
            }
        }

        private async void CreateAsaAnchorEditor(Transform indicatorTransform)
        {
            var indicator = Instantiate(anchorPositionPrefab, indicatorTransform.position, indicatorTransform.rotation);
            anchorCreationController.StartProgressIndicatorSession();
            await Task.Delay(2500);
            indicator.Init(currentTrackedObject);
            var mockAnchorId = Guid.NewGuid().ToString();
            activeAnchors.Add(currentTrackedObject.SpatialAnchorId, indicator);
            OnCreateAnchorSucceeded?.Invoke(this, mockAnchorId);
            currentTrackedObject = null;
        }

        private async void CreateAsaAnchor(Transform indicatorTransform)
        {
            Debug.Log("\nAnchorManager.CreateAsaAnchor()");
            anchorCreationController.StartProgressIndicatorSession();

            if (cloudManager.Session == null)
            {
                // Creates a new session if one does not exist
                await cloudManager.CreateSessionAsync();
            }

            // Starts the session if not already started
            await cloudManager.StartSessionAsync();
            
            var anchorPosition = Instantiate(anchorPositionPrefab, indicatorTransform.position, indicatorTransform.rotation);

            // Create native XR anchor at the location of the object
            anchorPosition.gameObject.CreateNativeAnchor();

            // Create local cloud anchor
            var localCloudAnchor = new CloudSpatialAnchor();

            // Set the local cloud anchor's position to the native XR anchor's position
            localCloudAnchor.LocalAnchor = anchorPosition.gameObject.FindNativeAnchor().GetPointer();

            // Check to see if we got the local XR anchor pointer
            if (localCloudAnchor.LocalAnchor == IntPtr.Zero)
            {
                Debug.Log("Didn't get the local anchor...");
                return;
            }
            else
            {
                Debug.Log("Local anchor created");
            }

            // Set expiration (when anchor will be deleted from Azure)
            localCloudAnchor.Expiration = DateTimeOffset.Now.AddDays(7);

            // Save anchor to cloud
            while (!cloudManager.IsReadyForCreate)
            {
                await Task.Delay(330);
                var createProgress = cloudManager.SessionStatus.RecommendedForCreateProgress;
                UnityDispatcher.InvokeOnAppThread(() => Debug.Log($"Move your device to capture more environment data: {createProgress:0%}"));
            }

            try
            {
                // Actually save
                await cloudManager.CreateAnchorAsync(localCloudAnchor);

                // Store
                currentCloudAnchor = localCloudAnchor;

                // Success?
                var success = currentCloudAnchor != null;

                if (success)
                {
                    Debug.Log($"Azure anchor with ID '{currentCloudAnchor.Identifier}' created successfully");

                    // Update the current Azure anchor ID
                    Debug.Log($"Current Azure anchor ID updated to '{currentCloudAnchor.Identifier}'");

                    activeAnchors.Add(currentTrackedObject.SpatialAnchorId, anchorPosition);

                    // Notify subscribers
                    OnCreateAnchorSucceeded?.Invoke(this, currentCloudAnchor.Identifier);
                }
                else
                {
                    Debug.Log($"Failed to save cloud anchor with ID '{currentCloudAnchor.Identifier}' to Azure");

                    // Notify subscribers
                    OnCreateAnchorFailed?.Invoke(this, EventArgs.Empty);
                }
                
                currentTrackedObject = null;
            }
            catch (Exception ex)
            {
                Debug.Log(ex.ToString());
            }

            StopAzureSession();
        }

        private async void FindAsaAnchor()
        {
            Debug.Log("\nAnchorManager.FindAsaAnchor()");
            anchorCreationController.StartProgressIndicatorSession();

            if (cloudManager.Session == null)
            {
                // Creates a new session if one does not exist
                await cloudManager.CreateSessionAsync();
            }

            // Starts the session if not already started
            await cloudManager.StartSessionAsync();

            // Create list of anchor IDs to locate
            var anchorsToFind = new List<string> { currentTrackedObject.SpatialAnchorId };

            anchorLocateCriteria = new AnchorLocateCriteria { Identifiers = anchorsToFind.ToArray() };

            // Start watching for Anchors
            if (cloudManager != null && cloudManager.Session != null)
            {
                currentWatcher = cloudManager.Session.CreateWatcher(anchorLocateCriteria);
            }
            else
            {
                Debug.Log("Attempt to create watcher failed, no session exists");
                currentWatcher = null;
            }
        }

        private async void FindAsaAnchorEditor()
        {
            anchorCreationController.StartProgressIndicatorSession();
            await Task.Delay(3000);
            
            var targetPosition = Camera.main.transform.position;
            targetPosition.z += 0.5f;
            var indicator = Instantiate(anchorPositionPrefab);
            indicator.transform.position = targetPosition;
            indicator.Init(currentTrackedObject);
            anchorArrowGuide.SetTargetObject(indicator.transform);

            // Notify subscribers
            activeAnchors.Add(currentTrackedObject.SpatialAnchorId, indicator);
            OnFindAnchorSucceeded?.Invoke(this, EventArgs.Empty);
            currentTrackedObject = null;
        }

        private async void StopAzureSession()
        {
            // Reset the current session if there is one, and wait for any active queries to be stopped
            await cloudManager.ResetSessionAsync();

            // Stop any existing session
            cloudManager.StopSession();
        }
        
        #region EVENT HANDLERS
        private void HandleAnchorLocated(object sender, AnchorLocatedEventArgs args)
        {
            Debug.Log($"Anchor recognized as a possible Azure anchor");
            
            if (args.Status == LocateAnchorStatus.Located || args.Status == LocateAnchorStatus.AlreadyTracked)
            {
                currentCloudAnchor = args.Anchor;
                Debug.Log($"Azure anchor located successfully");
                var indicator = Instantiate(anchorPositionPrefab);

#if WINDOWS_UWP || UNITY_WSA
                indicator.gameObject.CreateNativeAnchor();

                if (currentCloudAnchor == null)
                {
                    return;
                }
                Debug.Log("Local anchor position successfully set to Azure anchor position");

                indicator.GetComponent<UnityEngine.XR.WSA.WorldAnchor>().SetNativeSpatialAnchorPtr(currentCloudAnchor.LocalAnchor);
#elif UNITY_ANDROID || UNITY_IOS
                Pose anchorPose = Pose.identity;
                anchorPose = currentCloudAnchor.GetPose();

                Debug.Log($"Setting object to anchor pose with position '{anchorPose.position}' and rotation '{anchorPose.rotation}'");
                indicator.transform.position = anchorPose.position;
                indicator.transform.rotation = anchorPose.rotation;

                // Create a native anchor at the location of the object in question
                indicator.gameObject.CreateNativeAnchor();
#endif
                
                indicator.Init(currentTrackedObject);
                anchorArrowGuide.SetTargetObject(indicator.transform);
                activeAnchors.Add(currentTrackedObject.SpatialAnchorId, indicator);
                
                // Notify subscribers
                OnFindAnchorSucceeded?.Invoke(this, EventArgs.Empty);
                currentTrackedObject = null;
            }
            else
            {
                Debug.Log($"Attempt to locate Anchor with ID '{args.Identifier}' failed, locate anchor status was not 'Located' but '{args.Status}'");
            }

            StopAzureSession();
        }
        
        private void HandleOnAnchorPlaced(object sender, Transform indicatorTransform)
        {
            anchorPlacementController.gameObject.SetActive(false);
            CreateAnchor(indicatorTransform);
        }

        private void HandleOnAnchorPlacementCanceled(object sender, EventArgs e)
        {
            OnPlaceAnchorCanceled?.Invoke(this, EventArgs.Empty);
            anchorPlacementController.gameObject.SetActive(false);
        }
        #endregion
    }
}
