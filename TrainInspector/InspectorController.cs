using System;
using System.Collections.Generic;
using DV.InventorySystem;
using DV.Simulation.Brake;
using DV.UI;
using UnityEngine;

namespace TrainInspector
{
    public sealed class InspectorController : MonoBehaviour
    {
        private sealed class NearbyTrain
        {
            internal Trainset Trainset;
            internal TrainCar PreferredCar;
            internal float Distance;
        }

        private const int WindowId = 190997;
        private const float MaximumSelectionDistance = 300f;

        private readonly List<NearbyTrain> nearbyTrains = new List<NearbyTrain>();
        private InspectionSession session;
        private InspectionType selectedType = InspectionType.FullDeparture;
        private InspectionPhase lastLoggedPhase;
        private Rect windowRect;
        private Vector2 mainScroll;
        private Vector2 selectionScroll;
        private Vector2 walkaroundScroll;
        private Vector2 carScroll;
        private float nextNearbyRefresh;
        private bool windowVisible;
        private bool helpGuideExpanded = true;
        private int selectedCarIndex;
        private string actionMessage;
        private bool actionMessageIsFailure;
        private InspectionCertificateData viewingCertificate;

        private CustomFirstPersonController cachedPlayerController;
        private bool rightClickLookActive;
        private readonly List<Texture2D> generatedTextures = new List<Texture2D>();

        private enum InspectionSectionView
        {
            All,
            BeforeStartup,
            StartupAndCabSetup,
            BrakeTestAndDeparture
        }

        private InspectionSectionView sectionView = InspectionSectionView.All;

        private GUIStyle windowStyle;
        private GUIStyle bannerBoxStyle;
        private GUIStyle letterheadTitleStyle;
        private GUIStyle letterheadSubtitleStyle;
        private GUIStyle sheetCardStyle;
        private GUIStyle innerCardStyle;
        private GUIStyle ledgerKeyStyle;
        private GUIStyle sectionHeaderStyle;
        private GUIStyle sectionSubheaderStyle;
        private GUIStyle paperButtonStyle;
        private GUIStyle primaryActionButtonStyle;
        private GUIStyle dangerButtonStyle;
        private GUIStyle checklistButtonStyle;
        private GUIStyle checklistButtonCheckedStyle;
        private GUIStyle checklistButtonDefectStyle;
        private GUIStyle tabActiveStyle;
        private GUIStyle tabInactiveStyle;
        private GUIStyle carCompletedButtonStyle;
        private GUIStyle carCompletedSelectedButtonStyle;
        private GUIStyle carCompletedBannerStyle;
        private GUIStyle stampCertificatePassedStyle;
        private GUIStyle stampCertificateFailedStyle;
        private GUIStyle certTitleStyle;
        private GUIStyle certSummaryStyle;
        private GUIStyle certStatusStyle;

        private GUIStyle headerStyle;
        private GUIStyle subheaderStyle;
        private GUIStyle wrappedStyle;
        private GUIStyle mutedStyle;
        private GUIStyle readyStyle;
        private GUIStyle warningStyle;
        private GUIStyle failureStyle;
        private GUIStyle notTestedStyle;
        private GUIStyle passBoxStyle;
        private GUIStyle failBoxStyle;

        public bool IsWindowVisible
        {
            get { return windowVisible; }
        }

        private void Awake()
        {
            float width = Mathf.Min(960f, Mathf.Max(520f, Screen.width - 30f));
            float height = Mathf.Min(840f, Mathf.Max(460f, Screen.height - 30f));
            windowRect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f,
                width, height);
            lastLoggedPhase = InspectionPhase.Charging;
            PlayerManager.PlayerChanged += OnPlayerChanged;
        }

        private void OnPlayerChanged()
        {
            cachedPlayerController = null;
            if (windowVisible)
            {
                SuspendPlayerControls();
            }
        }

        private void Update()
        {
            if (Main.Settings != null && Main.Settings.ToggleKey != null && Main.Settings.ToggleKey.Down())
            {
                SetWindowVisible(!windowVisible);
            }

            if (windowVisible)
            {
                CustomFirstPersonController controller = GetPlayerController();
                if (controller != null && controller.Locomotion != null)
                {
                    bool allowMovement = InspectorInventoryItem.IsClipboardOpen;
                    if (controller.Locomotion.inputEnabled != allowMovement)
                    {
                        controller.Locomotion.inputEnabled = allowMovement;
                    }
                }

                if (Input.GetMouseButtonDown(1))
                {
                    if (controller != null)
                    {
                        controller.FreeLookAllowed = true;
                        if (controller.m_MouseLook != null)
                        {
                            controller.m_MouseLook.RemoveRequest(this);
                        }
                    }
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    rightClickLookActive = true;
                }
                else if (Input.GetMouseButtonUp(1) && rightClickLookActive)
                {
                    if (controller != null)
                    {
                        controller.FreeLookAllowed = false;
                        if (controller.m_MouseLook != null)
                        {
                            controller.m_MouseLook.RequestMouseSensitivityState(this, MouseSensitivityState.Locked, 100);
                        }
                    }
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    rightClickLookActive = false;
                }
                else if (!rightClickLookActive)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }

            float elapsed = Time.unscaledDeltaTime;
            if (session != null)
            {
                session.Tick(elapsed);
                if (session.Phase != lastLoggedPhase)
                {
                    lastLoggedPhase = session.Phase;
                    if (session.Phase == InspectionPhase.Invalidated)
                    {
                        Main.ModEntry.Logger.Warning("Inspection invalidated: " + session.InvalidReason);
                    }
                    else if (session.Phase == InspectionPhase.Complete && session.Report != null)
                    {
                        Main.ModEntry.Logger.Log(session.Report.Summary);
                    }
                }
            }

            PenaltyManager.Tick(session, elapsed);
        }

        private void OnGUI()
        {
            if (!windowVisible)
            {
                return;
            }

            EnsureStyles();
            ClampWindowToScreen();
            windowRect = GUI.Window(WindowId, windowRect, new GUI.WindowFunction(DrawWindow),
                "DERAIL VALLEY RAILROAD — MECHANICAL DEPARTMENT  [FORM MP-223]", windowStyle);
        }

        private void OnDestroy()
        {
            PlayerManager.PlayerChanged -= OnPlayerChanged;
            if (session != null)
            {
                session.Dispose();
                session = null;
            }

            ResumePlayerControls();
            CleanupGeneratedTextures();
        }

        public void SetWindowVisible(bool visible)
        {
            if (windowVisible == visible)
            {
                return;
            }

            windowVisible = visible;
            if (visible)
            {
                SuspendPlayerControls();
                nextNearbyRefresh = 0f;
            }
            else
            {
                ResumePlayerControls();
                InspectorInventoryItem.NotifyWindowClosed();
            }
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical(bannerBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("DERAIL VALLEY RAILROAD  ★  MECHANICAL & EQUIPMENT DEPARTMENT", letterheadTitleStyle);
            GUILayout.Label("FORM MP-223 (REV. 99.7) : FREIGHT TRAIN AIR BRAKE & MECHANICAL INSPECTION RECORD", letterheadSubtitleStyle);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            Inventory inventory = Inventory.Instance;
            if (inventory != null && !InspectorInventoryItemManager.HasInspectionClipboard(inventory))
            {
                if (GUILayout.Button("📋 Add Clipboard", primaryActionButtonStyle, GUILayout.Width(135f), GUILayout.Height(30f)))
                {
                    InspectorInventoryItemManager.EnsureClipboardNow();
                }
                GUILayout.Space(6f);
            }
            string toggleKeyText = Main.Settings != null ? Main.Settings.ToggleKeyDisplay : "F7";
            if (GUILayout.Button("✕ Close (" + toggleKeyText + ")", paperButtonStyle, GUILayout.Width(120f), GUILayout.Height(30f)))
            {
                SetWindowVisible(false);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            DrawRuntimeAlerts();
            GUILayout.Space(2f);

            mainScroll = GUILayout.BeginScrollView(mainScroll);
            if (viewingCertificate != null)
            {
                DrawCertificateView(viewingCertificate);
            }
            else if (session == null)
            {
                DrawTrainSelection();
            }
            else
            {
                DrawInspection();
            }

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width - 120f, 38f));
        }

        private void DrawRuntimeAlerts()
        {
            if (!Main.Settings.ShowDepartureWarnings)
            {
                return;
            }

            if (PenaltyManager.RollingInspectionDue)
            {
                GUILayout.Box("A post-departure rolling inspection is due. Stop safely and select that inspection type.",
                    failBoxStyle);
            }

            PenaltyEvent penalty = PenaltyManager.LastPenalty;
            if (penalty != null)
            {
                GUILayout.Label(penalty.OccurredAt.ToString("HH:mm:ss") + " - " + penalty.Summary,
                    failureStyle);
            }

            if (!string.IsNullOrEmpty(FaultRegistry.LastEvent)
                && Time.unscaledTime - FaultRegistry.LastEventAt <= 30f)
            {
                GUILayout.Label(FaultRegistry.LastEvent, failureStyle);
            }
        }

        private void DrawTrainSelection()
        {
            TrainCar occupied = SafePlayerCar();
            Trainset occupiedTrainset = occupied == null ? null : occupied.trainset;
            string occupiedSignature = occupiedTrainset == null ? null : TrainIdentity.BuildConsistSignature(occupiedTrainset);

            InspectionCertificateData activeCert = occupiedTrainset == null
                ? null
                : InspectionCertificateRegistry.GetValidDepartureCertificate(occupiedTrainset);

            if (activeCert != null)
            {
                DrawActiveCertificateBanner(activeCert, occupied);
                GUILayout.Space(6f);
            }

            ActiveSessionData savedSession = InspectionPersistence.Data != null
                ? InspectionPersistence.Data.ActiveSession
                : null;

            if (savedSession != null)
            {
                bool matchesOccupied = !string.IsNullOrEmpty(occupiedSignature)
                    && savedSession.ConsistSignature == occupiedSignature;
                DrawResumeSessionCard(savedSession, matchesOccupied, occupiedTrainset, occupied);
                GUILayout.Space(6f);
            }

            Inventory inventory = Inventory.Instance;
            if (inventory != null && !InspectorInventoryItemManager.HasInspectionClipboard(inventory))
            {
                GUILayout.BeginHorizontal(bannerBoxStyle);
                GUILayout.Label("Inspection Clipboard is not currently in your inventory.", wrappedStyle);
                if (GUILayout.Button("📋 Spawn Clipboard to Inventory", primaryActionButtonStyle, GUILayout.Width(235f), GUILayout.Height(32f)))
                {
                    InspectorInventoryItemManager.EnsureClipboardNow();
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(6f);
            }

            GUILayout.BeginVertical(sheetCardStyle);
            GUILayout.Label("PART I: SELECT INSPECTION TYPE", sectionHeaderStyle);
            GUILayout.Label("Select the required FRA / Railroad inspection procedure for this train.", wrappedStyle);
            GUILayout.Space(4f);
            DrawInspectionTypeChoice(InspectionType.FullDeparture);
            DrawInspectionTypeChoice(InspectionType.Continuity);
            DrawInspectionTypeChoice(InspectionType.CabChange);
            DrawInspectionTypeChoice(InspectionType.QuickSetAndRelease);
            DrawInspectionTypeChoice(InspectionType.Rolling);
            GUILayout.EndVertical();

            GUILayout.Space(6f);
            GUILayout.BeginVertical(sheetCardStyle);
            GUILayout.Label("PART II: SELECT TRAIN CONSIST", sectionHeaderStyle);
            GUILayout.Label("The occupied train is preferred. Nearby consists are listed by their closest car.",
                wrappedStyle);
            GUILayout.Space(4f);

            if (occupied != null && occupiedTrainset != null)
            {
                string occupiedBtnText = (activeCert != null ? "Re-inspect occupied train (Currently Certified) | " : "Inspect occupied train | ")
                                         + TrainIdentity.SafeCarId(occupied) + " | "
                                         + occupiedTrainset.cars.Count + " cars";
                GUIStyle btnStyle = activeCert != null ? paperButtonStyle : primaryActionButtonStyle;
                if (GUILayout.Button(occupiedBtnText, btnStyle, GUILayout.Height(38f)))
                {
                    if (activeCert != null)
                    {
                        InspectionCertificateRegistry.Revoke(activeCert);
                    }
                    StartInspection(occupiedTrainset, occupied, false, selectedType);
                    return;
                }

                GUILayout.Space(5f);
            }

            RefreshNearbyTrainsIfNeeded();
            if (nearbyTrains.Count == 0)
            {
                GUILayout.Box("No active trainsets are within " + MaximumSelectionDistance.ToString("0")
                              + " m. Load a save or walk closer to a train.", innerCardStyle);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Label("Nearby trains (nearest first):", sectionSubheaderStyle);
            selectionScroll = GUILayout.BeginScrollView(selectionScroll, GUILayout.MinHeight(160f),
                GUILayout.MaxHeight(320f));
            for (int index = 0; index < nearbyTrains.Count; index++)
            {
                NearbyTrain nearby = nearbyTrains[index];
                if (nearby.Trainset == null || nearby.Trainset.cars == null || nearby.Trainset.cars.Count == 0)
                {
                    continue;
                }

                InspectionCertificateData nearbyCert = InspectionCertificateRegistry.GetValidDepartureCertificate(nearby.Trainset);
                string prefix = nearbyCert != null ? "[Certified] " : string.Empty;
                if (GUILayout.Button(prefix + "Inspect | " + nearby.Distance.ToString("0") + " m | "
                                     + nearby.Trainset.cars.Count + " cars | "
                                     + DescribeEndpoints(nearby.Trainset), paperButtonStyle, GUILayout.Height(36f)))
                {
                    if (nearbyCert != null)
                    {
                        InspectionCertificateRegistry.Revoke(nearbyCert);
                    }
                    StartInspection(nearby.Trainset, nearby.PreferredCar, false, selectedType);
                    break;
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawActiveCertificateBanner(InspectionCertificateData cert, TrainCar occupied)
        {
            GUILayout.BeginVertical(stampCertificatePassedStyle);
            GUILayout.Label("★ ACTIVE INSPECTION CERTIFICATE — FORM MP-223 COMPLIANT ★", certTitleStyle);
            GUILayout.Space(2f);
            GUILayout.Label("This consist has a valid " + InspectionTypeRules.DisplayName((InspectionType)cert.InspectionTypeValue)
                            + " on file and is fully cleared for train operations.", readyStyle);
            GUILayout.Space(4f);

            GUILayout.BeginVertical(innerCardStyle);
            GUILayout.Label("Certificate ID: " + cert.Id, ledgerKeyStyle);
            if (occupied != null)
            {
                GUILayout.Label("Consist: " + TrainIdentity.SafeCarId(occupied) + " (" + cert.CarCount + " cars)", ledgerKeyStyle);
            }
            GUILayout.Label("Inspection Type: " + InspectionTypeRules.DisplayName((InspectionType)cert.InspectionTypeValue), ledgerKeyStyle);
            GUILayout.Label("Remaining Validity: " + cert.RemainingHours.ToString("0.0") + " operating hours", readyStyle);
            if (!string.IsNullOrEmpty(cert.Summary))
            {
                GUILayout.Label("Summary: " + cert.Summary, wrappedStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("📄 Review Full Certificate Sheet", primaryActionButtonStyle, GUILayout.Height(36f)))
            {
                viewingCertificate = cert;
            }

            if (occupied != null && occupied.trainset != null)
            {
                if (GUILayout.Button("↺ Re-Inspect Consist (New Test)", paperButtonStyle, GUILayout.Height(36f)))
                {
                    InspectionCertificateRegistry.Revoke(cert);
                    StartInspection(occupied.trainset, occupied, false, selectedType);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawResumeSessionCard(ActiveSessionData saved, bool matchesOccupied, Trainset occupiedTrainset, TrainCar occupied)
        {
            GUILayout.BeginVertical(bannerBoxStyle);
            GUILayout.Label("⏱ SAVED IN-PROGRESS INSPECTION FOUND", letterheadTitleStyle);
            InspectionType savedType = (InspectionType)saved.TypeValue;
            InspectionPhase savedPhase = (InspectionPhase)saved.PhaseValue;
            GUILayout.Label("A previous inspection was saved mid-progress ("
                            + InspectionTypeRules.DisplayName(savedType) + " — Phase: " + savedPhase + ").", letterheadSubtitleStyle);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            if (matchesOccupied && occupiedTrainset != null)
            {
                if (GUILayout.Button("▶ Resume In-Progress Inspection", primaryActionButtonStyle, GUILayout.Height(36f)))
                {
                    session = InspectionSession.Restore(occupiedTrainset, saved);
                    if (session != null)
                    {
                        selectedType = session.Type;
                        lastLoggedPhase = session.Phase;
                        SetActionMessage("Inspection resumed from saved game.", false);
                    }
                }
            }
            else
            {
                GUILayout.Label("(Board the train associated with this inspection to resume.)", mutedStyle);
            }

            if (GUILayout.Button("✕ Discard Saved Progress", dangerButtonStyle, GUILayout.Height(36f), GUILayout.Width(190f)))
            {
                InspectionSession.ClearActiveState();
                SetActionMessage("Saved in-progress inspection discarded.", false);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawCertificateView(InspectionCertificateData cert)
        {
            GUILayout.BeginVertical(sheetCardStyle);
            GUILayout.BeginVertical(stampCertificatePassedStyle);
            GUILayout.Label("★ DERAIL VALLEY RAILROAD — AIR BRAKE & EQUIPMENT CERTIFICATION ★", certTitleStyle);
            GUILayout.Label("FORM MP-223 : OFFICIAL TRAIN INSPECTION CERTIFICATE", certSummaryStyle);
            GUILayout.Space(6f);
            if (!string.IsNullOrEmpty(cert.Summary))
            {
                GUILayout.Label(cert.Summary, certSummaryStyle);
                GUILayout.Space(4f);
            }

            DateTime issued = new DateTime(cert.IssuedUtcTicks, DateTimeKind.Utc).ToLocalTime();
            DateTime expires = new DateTime(cert.ExpiresUtcTicks, DateTimeKind.Utc).ToLocalTime();
            GUILayout.Label("CERTIFICATE ID: " + cert.Id, ledgerKeyStyle);
            GUILayout.Label("ISSUED: " + issued.ToString("yyyy-MM-dd HH:mm:ss") + " | RECORDED EXPIRES: " + expires.ToString("yyyy-MM-dd HH:mm:ss"), mutedStyle);
            GUILayout.Label("IN-GAME REMAINING OPERATING TIME: " + cert.RemainingHours.ToString("0.0") + " hours", readyStyle);
            GUILayout.Label("CONSIST: " + cert.CarCount + " cars | INSPECTION TYPE: "
                            + InspectionTypeRules.DisplayName((InspectionType)cert.InspectionTypeValue), ledgerKeyStyle);
            GUILayout.EndVertical();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀ Back to Train Selection", primaryActionButtonStyle, GUILayout.Height(36f)))
            {
                viewingCertificate = null;
            }

            TrainCar occupied = SafePlayerCar();
            if (occupied != null && occupied.trainset != null
                && TrainIdentity.BuildConsistSignature(occupied.trainset) == cert.ConsistSignature)
            {
                if (GUILayout.Button("↺ Re-Inspect This Train", paperButtonStyle, GUILayout.Height(36f)))
                {
                    InspectionCertificateRegistry.Revoke(cert);
                    viewingCertificate = null;
                    StartInspection(occupied.trainset, occupied, false, selectedType);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawInspectionTypeChoice(InspectionType type)
        {
            bool isSelected = selectedType == type;
            string prefix = isSelected ? "★ " : "  ";
            if (type == InspectionType.Rolling && PenaltyManager.RollingInspectionDue)
            {
                prefix += "[DUE] ";
            }

            GUIStyle btnStyle = isSelected ? primaryActionButtonStyle : paperButtonStyle;
            if (GUILayout.Button(prefix + InspectionTypeRules.DisplayName(type) + " — "
                                 + InspectionTypeRules.ShortDescription(type), btnStyle, GUILayout.Height(38f)))
            {
                selectedType = type;
            }
        }

        private void DrawInspection()
        {
            DrawSessionHeader();
            if (session == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(actionMessage))
            {
                GUILayout.Label(actionMessage, actionMessageIsFailure ? failureStyle : readyStyle);
            }

            DrawConsistDefectsBanner();
            DrawSectionNavigation();
            DrawCurrentTaskGuide();
            GUILayout.Space(5f);

            bool showS1 = sectionView == InspectionSectionView.All || sectionView == InspectionSectionView.BeforeStartup;
            bool showS2 = sectionView == InspectionSectionView.All || sectionView == InspectionSectionView.StartupAndCabSetup;
            bool showS3 = sectionView == InspectionSectionView.All || sectionView == InspectionSectionView.BrakeTestAndDeparture;

            if (showS1)
            {
                DrawSection1BeforeStartup();
                GUILayout.Space(5f);
            }

            if (showS2 && InspectionTypeRules.RequiresCabSetup(session.Type))
            {
                DrawSection2StartupAndCab();
                GUILayout.Space(5f);
            }

            if (showS3)
            {
                DrawSection3BrakeTest();
            }
        }

        private void DrawSectionNavigation()
        {
            bool s1Done = session.WalkaroundCheckedCount >= session.WalkaroundRequiredCount;
            bool s2Done = !InspectionTypeRules.RequiresCabSetup(session.Type)
                || (session.CabSetupInspected && session.CabPassedItems == CabSetupItem.All);
            bool s3Done = session.Phase == InspectionPhase.Complete;

            GUILayout.BeginHorizontal();
            string allPrefix = sectionView == InspectionSectionView.All ? "★ " : "";
            if (GUILayout.Button(allPrefix + "ALL SECTIONS",
                sectionView == InspectionSectionView.All ? tabActiveStyle : tabInactiveStyle, GUILayout.Height(32f)))
            {
                sectionView = InspectionSectionView.All;
            }

            string s1Prefix = sectionView == InspectionSectionView.BeforeStartup ? "★ " : "";
            string s1Text = s1Prefix + "1. BEFORE START-UP " + (s1Done ? "(✓ DONE)" : "(" + session.WalkaroundCheckedCount + "/" + session.WalkaroundRequiredCount + ")");
            if (GUILayout.Button(s1Text,
                sectionView == InspectionSectionView.BeforeStartup ? tabActiveStyle : tabInactiveStyle, GUILayout.Height(32f)))
            {
                sectionView = InspectionSectionView.BeforeStartup;
            }

            if (InspectionTypeRules.RequiresCabSetup(session.Type))
            {
                string s2Prefix = sectionView == InspectionSectionView.StartupAndCabSetup ? "★ " : "";
                string s2Text = s2Prefix + "2. START-UP & CAB " + (s2Done ? "(✓ PASSED)" : "(⏱ PENDING)");
                if (GUILayout.Button(s2Text,
                    sectionView == InspectionSectionView.StartupAndCabSetup ? tabActiveStyle : tabInactiveStyle, GUILayout.Height(32f)))
                {
                    sectionView = InspectionSectionView.StartupAndCabSetup;
                }
            }

            string s3Prefix = sectionView == InspectionSectionView.BrakeTestAndDeparture ? "★ " : "";
            string s3Text = s3Prefix + "3. BRAKE TEST " + (s3Done ? "(✓ CERTIFIED)" : "[" + session.Phase.ToString().ToUpper() + "]");
            if (GUILayout.Button(s3Text,
                sectionView == InspectionSectionView.BrakeTestAndDeparture ? tabActiveStyle : tabInactiveStyle, GUILayout.Height(32f)))
            {
                sectionView = InspectionSectionView.BrakeTestAndDeparture;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        private void DrawSection1BeforeStartup()
        {
            bool s1Done = session.WalkaroundCheckedCount >= session.WalkaroundRequiredCount;
            GUILayout.BeginVertical(sheetCardStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("SECTION 1: BEFORE START-UP  (Ground & Train Walk-Around)", sectionHeaderStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(s1Done ? "[ ✓ COMPLETED ]" : "[ " + session.WalkaroundCheckedCount + " / " + session.WalkaroundRequiredCount + " OBSERVATIONS ]",
                s1Done ? readyStyle : warningStyle);
            GUILayout.EndHorizontal();
            GUILayout.Label("Inspect couplers, hoses, angle cocks, handbrakes, wheels/bearings, and brake rigging while walking alongside the train before engine start-up. Handbrakes may remain set to secure the train.", wrappedStyle);
            GUILayout.Space(3f);
            DrawWalkaroundPanel();
            GUILayout.EndVertical();
        }

        private void DrawSection2StartupAndCab()
        {
            bool passed = session.CabSetupInspected && session.CabPassedItems == CabSetupItem.All;
            GUILayout.BeginVertical(sheetCardStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("SECTION 2: START-UP & CAB SETUP  (Locomotive Air & In-Cab Preparation)", sectionHeaderStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(passed ? "[ ✓ VERIFIED ]" : "[ ⏱ PENDING ]", passed ? readyStyle : warningStyle);
            GUILayout.EndHorizontal();
            GUILayout.Label("Board the controlling cab. Start engines and compressor, verify main reservoir reaches >= "
                          + InspectionSession.MinimumMainReservoirPressure.ToString("0.0") + " bar. "
                          + "Set lead cab's cutout to CUT IN (and all trailing cabs/boosters to CUT OUT), release handbrakes and independent brake, charge air, and verify cylinders release.", wrappedStyle);
            GUILayout.Space(3f);
            DrawCabSetupPanel();
            GUILayout.EndVertical();
        }

        private void DrawSection3BrakeTest()
        {
            GUILayout.BeginVertical(sheetCardStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("SECTION 3: BRAKE TEST & DEPARTURE  (Functional Testing & Certification)", sectionHeaderStyle);
            GUILayout.FlexibleSpace();
            string statusText = session.Phase == InspectionPhase.Complete ? "[ ✓ CERTIFIED ]" : "[" + session.Phase.ToString().ToUpper() + "]";
            GUILayout.Label(statusText, session.Phase == InspectionPhase.Complete ? readyStyle : warningStyle);
            GUILayout.EndHorizontal();
            GUILayout.Label("Execute required functional air brake tests: Leakage hold, Service application, Release & recharge, "
                          + "Rear-car verification, and Departure certification.", wrappedStyle);
            GUILayout.Space(3f);

            switch (session.Phase)
            {
                case InspectionPhase.Charging:
                    if (session.Type == InspectionType.Rolling)
                    {
                        DrawRollingStep();
                    }
                    else
                    {
                        DrawChargingStep();
                    }
                    break;
                case InspectionPhase.Leakage:
                    DrawLeakageStep();
                    break;
                case InspectionPhase.Applying:
                    DrawApplicationStep();
                    break;
                case InspectionPhase.Releasing:
                    DrawReleaseStep();
                    break;
                case InspectionPhase.Complete:
                    DrawResult();
                    break;
                case InspectionPhase.Invalidated:
                    DrawInvalidated();
                    break;
            }

            GUILayout.EndVertical();
        }

        private void DrawCurrentTaskGuide()
        {
            if (session == null || session.Phase == InspectionPhase.Complete
                                || session.Phase == InspectionPhase.Invalidated)
            {
                return;
            }

            GUILayout.BeginVertical(innerCardStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("INSPECTION PROCEDURE & ACTIVE STEP", sectionSubheaderStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(helpGuideExpanded ? "▲ Hide details" : "▼ Show details", paperButtonStyle, GUILayout.Width(110f), GUILayout.Height(24f)))
            {
                helpGuideExpanded = !helpGuideExpanded;
            }

            GUILayout.EndHorizontal();

            string action = GetCurrentTask(out string detail, out bool readyToClick);
            GUILayout.Label("▶ " + action, readyToClick ? readyStyle : sectionSubheaderStyle);
            if (helpGuideExpanded)
            {
                if (!string.IsNullOrEmpty(detail))
                {
                    GUILayout.Label(detail, wrappedStyle);
                }

                if (session.Phase == InspectionPhase.Charging)
                {
                    DrawPreparationGuideStatus();
                }
            }

            GUILayout.EndVertical();
        }

        private string GetCurrentTask(out string detail, out bool readyToClick)
        {
            detail = string.Empty;
            readyToClick = false;

            if (session.Phase == InspectionPhase.Charging)
            {
                if (TryGetWalkaroundTask(out string walkaroundAction, out detail))
                {
                    return "[1. Before Start-up] " + walkaroundAction;
                }

                if (InspectionTypeRules.RequiresCabSetup(session.Type)
                    && !(session.CabSetupInspected && session.CabPassedItems == CabSetupItem.All))
                {
                    return "[2. Start-up & Cab Setup] " + GetCabSetupTask(out detail, out readyToClick);
                }

                if (session.Type == InspectionType.Rolling)
                {
                    readyToClick = true;
                    detail = "Every required side visit and equipment observation has been recorded.";
                    return "[3. Rolling Departure] Click Complete rolling inspection.";
                }

                if (InspectionTypeRules.RequiresLeakageTest(session.Type) && !session.LeakageCompleted)
                {
                    bool canBegin = session.CanBeginLeakage(out string reason);
                    if (canBegin)
                    {
                        readyToClick = true;
                        detail = "The brake pipe is charged and released. The next step isolates it to measure leakage.";
                        return "[3. Brake Test] Click Begin leakage hold.";
                    }

                    detail = "Leave the automatic brake handle in RELEASE while preparing the train.";
                    return "[3. Brake Test] " + reason;
                }

                bool canApply = session.CanBeginApplication(out string applicationReason);
                if (canApply)
                {
                    readyToClick = true;
                    detail = "This records the charged baseline before you move the automatic brake handle.";
                    return "[3. Brake Test] Click Record charge and begin service application.";
                }

                detail = "Keep the controlling train-brake valve CUT IN and the automatic brake handle in RELEASE.";
                return "[3. Brake Test] " + applicationReason;
            }

            if (session.Phase == InspectionPhase.Leakage)
            {
                CabSetupSnapshot cab = session.GetCabSetupSnapshot();
                if (cab.ControllingValveCutIn)
                {
                    detail = "Use the train-brake CUTOUT control, not the automatic brake handle. Do not change hoses "
                             + "or angle cocks during the hold.";
                    return "[3. Brake Test - Leakage] In the controlling cab, set the automatic/train-brake cutout to CUT OUT.";
                }

                if (!session.LeakageCutoutObserved)
                {
                    detail = "The timer starts automatically when the inspector detects CUT OUT.";
                    return "[3. Brake Test - Leakage] Keep the controlling valve CUT OUT and wait for the timer to start.";
                }

                if (session.LeakageElapsed < Main.Settings.LeakageHoldSeconds)
                {
                    int remaining = Mathf.CeilToInt(Main.Settings.LeakageHoldSeconds - session.LeakageElapsed);
                    detail = "Do not move the brake controls or change any brake-pipe connection during this hold.";
                    return "[3. Brake Test - Leakage] Keep the valve CUT OUT for " + remaining + " more second"
                           + (remaining == 1 ? "." : "s.");
                }

                bool canComplete = session.CanCompleteLeakage(out string leakageReason);
                if (canComplete)
                {
                    readyToClick = true;
                    detail = "After recording, set this same valve back to CUT IN and recharge the train.";
                    return "[3. Brake Test - Leakage] Click Record leakage result and return to charge.";
                }

                return "[3. Brake Test - Leakage] " + leakageReason;
            }

            if (session.Phase == InspectionPhase.Applying)
            {
                if (session.ServiceReduction < InspectionSession.MinimumServiceReduction)
                {
                    float remaining = InspectionSession.MinimumServiceReduction - session.ServiceReduction;
                    detail = "Charged pipe " + session.ChargedControlPipe.ToString("0.00") + " bar; current pipe "
                             + session.CurrentControlPipe.ToString("0.00") + " bar. Keep the brake applied and stable.";
                    return "[3. Brake Test - Application] Move the automatic brake into SERVICE and reduce the pipe another "
                           + remaining.ToString("0.00") + " bar.";
                }

                if (session.PhaseElapsed < 2f)
                {
                    detail = "Keep the automatic brake handle steady so the cylinders can respond.";
                    return "[3. Brake Test - Application] Hold the service application for " + Mathf.CeilToInt(2f - session.PhaseElapsed)
                           + " more second(s).";
                }

                if (InspectionTypeRules.RequiresRearVerification(session.Type)
                    && !session.RearApplicationConfirmed)
                {
                    bool canConfirm = session.CanConfirmRearApplication(out string reason);
                    CarInspectionRecord rear = session.RearRecord;
                    if (canConfirm)
                    {
                        readyToClick = true;
                        detail = "You are close enough and this car's brake cylinder has responded.";
                        return "[3. Brake Test - Rear Car] Click Confirm rear-car brake application for " + rear.Label + ".";
                    }

                    detail = "Leave the service application held while walking to the end farthest from the "
                             + "controlling cab.";
                    return "[3. Brake Test - Rear Car] " + reason;
                }

                readyToClick = true;
                detail = "The required reduction and rear check are complete.";
                return "[3. Brake Test - Application] Click Record application and begin release.";
            }

            if (session.Phase == InspectionPhase.Releasing)
            {
                if (InspectionTypeRules.RequiresRearVerification(session.Type) && !session.RearReleaseConfirmed)
                {
                    bool canConfirm = session.CanConfirmRearRelease(out string reason);
                    CarInspectionRecord rear = session.RearRecord;
                    if (canConfirm)
                    {
                        readyToClick = true;
                        detail = "You are close enough and this car's brake cylinder has released.";
                        return "[3. Brake Test - Rear Car] Click Confirm rear-car brake release for " + rear.Label + ".";
                    }

                    detail = "Keep the automatic brake fully in RELEASE and the compressor running while walking "
                             + "to the rear car.";
                    return "[3. Brake Test - Rear Car] " + reason;
                }

                bool canFinish = session.CanFinish(out string finishReason);
                if (canFinish)
                {
                    readyToClick = true;
                    detail = "The brake pipe is recharged and the required rear release has been confirmed.";
                    return "[3. Departure] Click Finish inspection and issue certificate.";
                }

                detail = "Keep the automatic brake fully in RELEASE and keep the compressor running.";
                return "[3. Departure] " + finishReason;
            }

            return "Follow the highlighted requirement below.";
        }

        private bool TryGetWalkaroundTask(out string action, out string detail)
        {
            action = string.Empty;
            detail = string.Empty;
            if (session.WalkaroundCheckedCount >= session.WalkaroundRequiredCount)
            {
                return false;
            }

            CarInspectionRecord next = session.GetNextUncheckedRecord();
            if (next == null)
            {
                return false;
            }

            selectedCarIndex = Mathf.Clamp(selectedCarIndex, 0, Mathf.Max(0, session.CarCount - 1));
            if (!session.CanInspect(next, out string rangeReason))
            {
                action = rangeReason;
                detail = "Select " + next.Label + " in the walk-around list. Side visits turn green automatically "
                         + "when you walk alongside the left or right side.";
                return true;
            }

            List<WalkaroundItem> missing = InspectionTypeRules.Expand(next.RequiredItems & ~next.CheckedItems);
            if (missing.Count == 0)
            {
                return false;
            }

            WalkaroundItem item = missing[0];
            if (item == WalkaroundItem.LeftSide || item == WalkaroundItem.RightSide)
            {
                string side = item == WalkaroundItem.LeftSide ? "left" : "right";
                action = "Walk along the " + side + " side of " + next.Label + ".";
                detail = "Stay within " + Main.Settings.WalkaroundRangeMetres.ToString("0")
                         + " m and move clearly beside the car; the side visit records automatically.";
            }
            else
            {
                action = "Visually check " + InspectionTypeRules.ItemLabel(item) + " on " + next.Label
                         + ", then click that checklist row.";
                detail = "The button records what you physically inspected; it does not operate or repair the equipment.";
            }

            return true;
        }

        private string GetCabSetupTask(out string detail, out bool readyToClick)
        {
            readyToClick = false;
            CabSetupSnapshot cab = session.GetCabSetupSnapshot();
            detail = CabCutoutExplanation();

            if (cab.ControlRecord == null)
            {
                return "No controlling locomotive was detected; choose the locomotive you are operating and restart.";
            }

            if (!cab.CanRecord)
            {
                return cab.RecordBlockReason;
            }

            if (!cab.HasBrakeSystem)
            {
                return "The selected controlling car has no usable brake system; choose the actual controlling locomotive.";
            }

            if (!cab.CompressorAvailable)
            {
                return "Use a controlling locomotive with a compressor.";
            }

            if (!cab.MainReservoirReady)
            {
                return "Run the locomotive and compressor until main reservoir reaches "
                       + InspectionSession.MinimumMainReservoirPressure.ToString("0.0") + " bar (currently "
                       + cab.MainReservoirPressure.ToString("0.0") + " bar).";
            }

            if (!cab.ControllingValveCutIn)
            {
                return "Set the controlling cab's automatic/train-brake CUTOUT control to CUT IN (active).";
            }

            if (!cab.ExactlyOneTrainBrakeValveCutIn)
            {
                return cab.EnabledTrainBrakeValves == 0
                    ? "No train-brake valve is active. Set this controlling cab's cutout control to CUT IN."
                    : "More than one train-brake valve is active. Set every other locomotive/cab to CUT OUT; leave "
                      + "only this cab CUT IN.";
            }

            if (cab.HasIndependentBrake && cab.IndependentBrakePosition > 0.05f)
            {
                return "Move the independent brake fully to RELEASE (currently "
                       + (cab.IndependentBrakePosition * 100f).ToString("0") + "% applied).";
            }

            if (cab.BrakeCylinderPressure > InspectionSession.ReleasedCylinderPressure)
            {
                return "Keep both automatic and independent brakes in RELEASE until the controlling brake cylinder "
                       + "falls to " + InspectionSession.ReleasedCylinderPressure.ToString("0.00")
                       + " bar or below (currently " + cab.BrakeCylinderPressure.ToString("0.00") + " bar).";
            }

            readyToClick = true;
            return session.CabSetupInspected
                ? "All cab conditions are now correct. Click Recheck cab setup to record them."
                : "All cab conditions are correct. Click Record cab setup now.";
        }

        private void DrawPreparationGuideStatus()
        {
            bool walkaroundDone = session.WalkaroundCheckedCount >= session.WalkaroundRequiredCount;
            DrawGuideStatus(walkaroundDone, "Walk-around", session.WalkaroundCheckedCount + " / "
                                                         + session.WalkaroundRequiredCount + " observations");

            if (InspectionTypeRules.RequiresCabSetup(session.Type))
            {
                bool cabDone = session.CabSetupInspected && session.CabPassedItems == CabSetupItem.All;
                DrawGuideStatus(cabDone, "Cab setup", cabDone ? "recorded and passed" : "not yet recorded as passed");
            }

            if (session.Type != InspectionType.Rolling)
            {
                string reason;
                bool brakeReady = InspectionTypeRules.RequiresLeakageTest(session.Type) && !session.LeakageCompleted
                    ? session.CanBeginLeakage(out reason)
                    : session.CanBeginApplication(out reason);
                DrawGuideStatus(brakeReady, "Brake train", brakeReady ? "charged and released" : reason);
            }
        }

        private void DrawGuideStatus(bool complete, string label, string status)
        {
            GUILayout.Label((complete ? "[OK] " : "[TO DO] ") + label + " - " + status,
                complete ? readyStyle : warningStyle);
        }

        private static string CabCutoutExplanation()
        {
            return "CUTOUT CONTROL: set the cab you are operating to CUT IN. Set every other automatic/train-brake "
                   + "valve connected to the same brake pipe to CUT OUT. Exactly one valve must be CUT IN. This is "
                   + "separate from placing the automatic brake handle in RELEASE.";
        }

        private void DrawSessionHeader()
        {
            GUILayout.BeginVertical(innerCardStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("CONSIST SPECIFICATION & DISPATCH LEDGER", sectionSubheaderStyle);
            GUILayout.FlexibleSpace();
            bool switchTrain = GUILayout.Button("✕ Switch Train", paperButtonStyle, GUILayout.Width(115f), GUILayout.Height(24f));
            GUILayout.EndHorizontal();

            if (switchTrain)
            {
                GUILayout.EndVertical();
                ClearSession();
                return;
            }

            string endpoints = session.CarCount == 0
                ? "No cars"
                : session.Cars[0].Id + " ➔ " + session.Cars[session.CarCount - 1].Id;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Rule: " + InspectionTypeRules.DisplayName(session.Type), ledgerKeyStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Length: " + session.CarCount + " Units (" + endpoints + ")", mutedStyle);
            GUILayout.EndHorizontal();

            CarInspectionRecord control = session.ControlRecord;
            GUILayout.BeginHorizontal();
            GUILayout.Label(control == null
                    ? "Controlling Unit: [NONE DETECTED]"
                    : "Controlling Unit: " + control.Label,
                control == null ? failureStyle : ledgerKeyStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Status: " + PhaseProgressText(), subheaderStyle);
            GUILayout.EndHorizontal();

            if (session.Phase == InspectionPhase.Charging)
            {
                if (GUILayout.Button("⇄ Reverse Car Numbering Order", paperButtonStyle, GUILayout.Width(235f), GUILayout.Height(24f)))
                {
                    RestartSession(!session.IsReversed);
                }
            }

            GUILayout.EndVertical();
        }

        private void DrawWalkaroundPanel()
        {
            if (session.WalkaroundRequiredCount == 0)
            {
                if (!Main.Settings.RequirePhysicalWalkaround)
                {
                    GUILayout.Label("Physical walk-around requirements are disabled in the mod settings.",
                        mutedStyle);
                }

                return;
            }

            int totalCarsCompleted = 0;
            for (int i = 0; i < session.Cars.Count; i++)
            {
                CarInspectionRecord carRec = session.Cars[i];
                if (carRec.MissingItemCount == 0 && carRec.RequiredItems != WalkaroundItem.None)
                {
                    totalCarsCompleted++;
                }
            }

            GUILayout.BeginVertical(innerCardStyle);
            GUILayout.Label("Walk-Around Equipment Checklist — " + totalCarsCompleted + " / " + session.Cars.Count
                            + " Cars Inspected  (" + session.WalkaroundCheckedCount + " / "
                            + session.WalkaroundRequiredCount + " Observations Recorded)", sectionSubheaderStyle);
            GUILayout.Label("Walk along both sides where required. Select a car while standing within "
                            + Main.Settings.WalkaroundRangeMetres.ToString("0")
                            + " m, then record each close-up equipment check.", wrappedStyle);

            CarInspectionRecord next = session.GetNextUncheckedRecord();
            if (Main.Settings.AssistedMode && next != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Next incomplete: " + next.Label, warningStyle);
                if (GUILayout.Button("Select next", paperButtonStyle, GUILayout.Width(95f), GUILayout.Height(24f)))
                {
                    selectedCarIndex = IndexOfRecord(next);
                }

                GUILayout.EndHorizontal();
            }

            walkaroundScroll = GUILayout.BeginScrollView(walkaroundScroll, GUILayout.MinHeight(90f),
                GUILayout.MaxHeight(160f));
            for (int index = 0; index < session.Cars.Count; index++)
            {
                CarInspectionRecord record = session.Cars[index];
                int completed = InspectionTypeRules.Expand(record.RequiredItems & record.CheckedItems).Count;
                int required = InspectionTypeRules.Expand(record.RequiredItems).Count;
                bool isDone = completed >= required && required > 0;
                bool isSelected = selectedCarIndex == index;
                string marker = isSelected ? "▶ " : "   ";
                string distance = record.DistanceToPlayer == float.MaxValue
                    ? "unavailable"
                    : record.DistanceToPlayer.ToString("0") + " m";
                string boTag = record.BadOrdered ? " [BAD ORDER ⚠]" : string.Empty;

                string buttonLabel;
                GUIStyle carBtnStyle;
                if (isDone)
                {
                    buttonLabel = marker + "✓ #" + record.Number.ToString("00") + " " + record.Id
                                  + " | [INSPECTED ✓] " + completed + "/" + required + boTag + " | Dist: " + distance;
                    carBtnStyle = isSelected ? carCompletedSelectedButtonStyle : carCompletedButtonStyle;
                }
                else
                {
                    buttonLabel = marker + "#" + record.Number.ToString("00") + " " + record.Id
                                  + " | Items: " + completed + "/" + required + boTag + " | Dist: " + distance;
                    carBtnStyle = isSelected ? primaryActionButtonStyle : paperButtonStyle;
                }

                if (GUILayout.Button(buttonLabel, carBtnStyle, GUILayout.Height(28f)))
                {
                    selectedCarIndex = index;
                }
            }

            GUILayout.EndScrollView();
            DrawSelectedCarInspection();
            GUILayout.EndVertical();
        }

        private void DrawSelectedCarInspection()
        {
            if (session.CarCount == 0)
            {
                return;
            }

            selectedCarIndex = Mathf.Clamp(selectedCarIndex, 0, session.CarCount - 1);
            CarInspectionRecord record = session.Cars[selectedCarIndex];
            int carCompleted = InspectionTypeRules.Expand(record.RequiredItems & record.CheckedItems).Count;
            int carRequired = InspectionTypeRules.Expand(record.RequiredItems).Count;
            bool isCarDone = carCompleted >= carRequired && carRequired > 0;

            if (isCarDone)
            {
                GUILayout.BeginVertical(carCompletedBannerStyle);
                GUILayout.Label("✓ CAR #" + record.Number.ToString("00") + " (" + record.Id + ") — ALL EQUIPMENT INSPECTED", readyStyle);
                GUILayout.Label("All " + carRequired + " required walk-around checks recorded. "
                                + (record.BadOrdered ? "⚠ Defects noted below." : "No visible defects found on this unit."), mutedStyle);
                GUILayout.EndVertical();
                GUILayout.Space(2f);
            }

            string reason;
            bool closeEnough = session.CanInspect(record, out reason);
            GUILayout.Space(4f);
            GUILayout.Label(record.Label + " — "
                            + (closeEnough ? "Within inspection range" : reason),
                closeEnough ? readyStyle : warningStyle);

            List<WalkaroundItem> required = InspectionTypeRules.Expand(record.RequiredItems);
            for (int index = 0; index < required.Count; index++)
            {
                WalkaroundItem item = required[index];
                bool complete = record.IsChecked(item);
                string checkPrefix = complete ? "[ ✓ ] " : "[   ] ";
                string label = checkPrefix + InspectionTypeRules.ItemLabel(item);

                if (item == WalkaroundItem.LeftSide || item == WalkaroundItem.RightSide)
                {
                    GUILayout.Label(label + (complete ? " (verified)" : " (walk alongside car)"), complete ? readyStyle : warningStyle);
                    continue;
                }

                GUI.enabled = closeEnough;
                GUIStyle itemStyle = complete ? checklistButtonCheckedStyle : checklistButtonStyle;
                if (GUILayout.Button(label + (complete ? "  (click to re-verify)" : string.Empty), itemStyle, GUILayout.Height(30f)))
                {
                    bool recorded = session.InspectItem(record, item, out string result);
                    SetActionMessage(result, !recorded || record.BadOrdered);
                    if (recorded && Main.Settings.AssistedMode && record.MissingItemCount == 0)
                    {
                        CarInspectionRecord following = session.GetNextUncheckedRecord();
                        if (following != null)
                        {
                            selectedCarIndex = IndexOfRecord(following);
                            SetActionMessage("✓ " + record.Label + " completed! Advancing to " + following.Label + ".", false);
                        }
                        else
                        {
                            SetActionMessage("★ All cars in consist have completed physical inspection! Proceed to cab setup.", false);
                        }
                    }
                }

                GUI.enabled = true;
            }

            GUI.enabled = closeEnough;
            GUIStyle boBtnStyle = record.BadOrdered ? paperButtonStyle : dangerButtonStyle;
            if (GUILayout.Button(record.BadOrdered ? "✓ Remove Manual Bad-Order Tag" : "⚠ Mark Car as Bad Order (Defect)",
                    boBtnStyle, GUILayout.Height(30f), GUILayout.Width(260f)))
            {
                session.ToggleBadOrder(record);
                SetActionMessage(record.BadOrdered
                    ? record.Label + " marked bad order."
                    : "Manual bad-order tag removed from " + record.Label + ".", record.BadOrdered);
            }

            GUI.enabled = true;

            for (int index = 0; index < record.PhysicalFindings.Count; index++)
            {
                GUILayout.Label("- " + record.PhysicalFindings[index], failureStyle);
            }

            DrawCarFaults(record);
        }

        private void DrawConsistDefectsBanner()
        {
            if (session == null || session.Cars == null || session.Cars.Count == 0)
            {
                return;
            }

            List<KeyValuePair<CarInspectionRecord, MechanicalFaultData>> defects =
                new List<KeyValuePair<CarInspectionRecord, MechanicalFaultData>>();
            int totalCost = 0;

            for (int i = 0; i < session.Cars.Count; i++)
            {
                CarInspectionRecord record = session.Cars[i];
                List<MechanicalFaultData> carFaults = FaultRegistry.GetFaults(record.Car, false);
                for (int j = 0; j < carFaults.Count; j++)
                {
                    defects.Add(new KeyValuePair<CarInspectionRecord, MechanicalFaultData>(record, carFaults[j]));
                    totalCost += FaultRegistry.GetRepairCost(carFaults[j]);
                }
            }

            if (defects.Count == 0)
            {
                return;
            }

            GUILayout.BeginVertical(failBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("⚠ DEFECTIVE EQUIPMENT FOUND (" + defects.Count + " BAD ORDER DEFECT"
                            + (defects.Count == 1 ? "" : "S") + ")", failureStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("🔧 Terminal Repair All Equipment ($" + totalCost + ")", primaryActionButtonStyle,
                    GUILayout.Height(28f)))
            {
                int repairedCount = 0;
                int billedTotal = 0;
                for (int i = 0; i < defects.Count; i++)
                {
                    CarInspectionRecord record = defects[i].Key;
                    MechanicalFaultData fault = defects[i].Value;
                    if (FaultRegistry.Repair(fault.Id, out int cost, out string reason))
                    {
                        repairedCount++;
                        billedTotal += cost;
                        session.SyncBadOrderState(record);
                    }
                }

                SetActionMessage("Repaired " + repairedCount + " defect(s) via terminal service. Billed $" + billedTotal + ".", false);
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Un-repaired Bad Order equipment fails departure inspection and triggers immediate catastrophic equipment failure upon departure.", wrappedStyle);

            for (int i = 0; i < defects.Count; i++)
            {
                CarInspectionRecord record = defects[i].Key;
                MechanicalFaultData fault = defects[i].Value;
                int cost = FaultRegistry.GetRepairCost(fault);
                GUILayout.BeginHorizontal();
                GUILayout.Label("• " + record.Label + ": " + FaultRegistry.Describe(fault)
                                + (fault.Active ? " [ACTIVE FAILURE]" : " [LATENT DEFECT]"), failureStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("🔧 Repair ($" + cost + ")", paperButtonStyle, GUILayout.Width(130f), GUILayout.Height(24f)))
                {
                    if (FaultRegistry.Repair(fault.Id, out int actualCost, out string reason))
                    {
                        session.SyncBadOrderState(record);
                        SetActionMessage(reason + " Terminal service billed $" + actualCost + ".", false);
                    }
                    else
                    {
                        SetActionMessage(reason, true);
                    }
                    break;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        private void DrawCarFaults(CarInspectionRecord record)
        {
            List<MechanicalFaultData> faults = FaultRegistry.GetFaults(record.Car, false);
            if (faults == null || faults.Count == 0)
            {
                return;
            }

            for (int index = 0; index < faults.Count; index++)
            {
                MechanicalFaultData fault = faults[index];
                int cost = FaultRegistry.GetRepairCost(fault);
                GUILayout.BeginHorizontal();
                GUILayout.Label("⚠ Defect: " + FaultRegistry.Describe(fault)
                                + (fault.Active ? " [ACTIVE FAILURE]" : " [LATENT DEFECT]"), failureStyle);
                bool repairClicked = false;
                if (GUILayout.Button("🔧 Terminal Repair ($" + cost + ")", primaryActionButtonStyle,
                        GUILayout.Width(220f), GUILayout.Height(28f)))
                {
                    bool repaired = FaultRegistry.Repair(fault.Id, out int actualCost, out string reason);
                    if (repaired && session != null)
                    {
                        session.SyncBadOrderState(record);
                    }
                    SetActionMessage(reason + (repaired ? " Terminal repair billed $" + actualCost + "." : string.Empty), !repaired);
                    repairClicked = true;
                }

                GUILayout.EndHorizontal();
                if (repairClicked)
                {
                    break;
                }
            }
        }

        private void DrawCabSetupPanel()
        {
            if (!InspectionTypeRules.RequiresCabSetup(session.Type))
            {
                return;
            }

            CabSetupSnapshot cab = session.GetCabSetupSnapshot();
            GUILayout.BeginVertical(innerCardStyle);
            GUILayout.Label("In-Cab Control & Locomotive Air Setup Checklist", sectionSubheaderStyle);
            GUILayout.Label(CabCutoutExplanation(), wrappedStyle);

            List<CarInspectionRecord> brakeCabs = new List<CarInspectionRecord>();
            for (int i = 0; i < session.Cars.Count; i++)
            {
                if (session.Cars[i].Car != null && TrainIdentity.HasTrainBrake(session.Cars[i].Car))
                {
                    brakeCabs.Add(session.Cars[i]);
                }
            }

            TrainCar occupied = SafePlayerCar();
            if (occupied != null && cab.ControlRecord != null && occupied != cab.ControlRecord.Car && TrainIdentity.HasTrainBrake(occupied))
            {
                if (GUILayout.Button("★ Switch controlling cab to current locomotive (" + TrainIdentity.SafeCarId(occupied) + ")", primaryActionButtonStyle, GUILayout.Height(30f)))
                {
                    string switchMsg;
                    session.TrySetControlCar(occupied, out switchMsg);
                    SetActionMessage(switchMsg, false);
                    cab = session.GetCabSetupSnapshot();
                }
                GUILayout.Space(3f);
            }

            if (brakeCabs.Count > 1)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Controlling locomotive:", ledgerKeyStyle, GUILayout.Width(170f));
                for (int i = 0; i < brakeCabs.Count; i++)
                {
                    CarInspectionRecord candidate = brakeCabs[i];
                    bool isCurrent = cab.ControlRecord != null && candidate.Car == cab.ControlRecord.Car;
                    if (isCurrent)
                    {
                        GUILayout.Label("[ " + candidate.Label + " (active) ]", readyStyle, GUILayout.Width(130f));
                    }
                    else
                    {
                        if (GUILayout.Button("Use " + candidate.Label, paperButtonStyle, GUILayout.Width(130f), GUILayout.Height(24f)))
                        {
                            string switchMsg;
                            session.TrySetControlCar(candidate.Car, out switchMsg);
                            SetActionMessage(switchMsg, false);
                            cab = session.GetCabSetupSnapshot();
                        }
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4f);
            }

            if (cab.ControlRecord == null)
            {
                DrawGuideStatus(false, "Controlling cab", "none detected");
            }
            else
            {
                DrawGuideStatus(cab.CanRecord, "Player position",
                    cab.CanRecord ? "standing near " + cab.ControlRecord.Label + " with the train stopped"
                        : cab.RecordBlockReason);
                DrawGuideStatus(cab.HasBrakeSystem, "Brake system",
                    cab.HasBrakeSystem ? "available on " + cab.ControlRecord.Label : "not available");
                DrawGuideStatus(cab.CompressorAvailable, "Compressor",
                    cab.CompressorAvailable
                        ? "fitted to the controlling locomotive"
                        : "not available on the selected controlling car");
                DrawGuideStatus(cab.MainReservoirReady, "Main reservoir",
                    cab.MainReservoirPressure.ToString("0.00") + " bar / minimum "
                    + InspectionSession.MinimumMainReservoirPressure.ToString("0.00") + " bar");
                DrawGuideStatus(cab.ControllingValveCutIn, "Controlling train-brake valve",
                    !cab.HasTrainBrake ? "no train-brake valve detected"
                        : cab.ControllingValveCutIn ? "CUT IN (active)" : "CUT OUT - change this cab to CUT IN");
                DrawGuideStatus(cab.ExactlyOneTrainBrakeValveCutIn, "Trailing train-brake valves",
                    cab.EnabledTrainBrakeValves + " valve(s) CUT IN; exactly 1 required"
                    + (cab.EnabledTrainBrakeValves > 1 ? " - set every other cab to CUT OUT" : string.Empty));
                DrawGuideStatus(cab.IndependentBrakeReleased, "Independent brake and cylinder",
                    (cab.HasIndependentBrake
                        ? (cab.IndependentBrakePosition * 100f).ToString("0") + "% handle application"
                        : "no independent-brake handle")
                    + " | cylinder " + cab.BrakeCylinderPressure.ToString("0.00") + " bar / maximum "
                    + InspectionSession.ReleasedCylinderPressure.ToString("0.00") + " bar");
            }

            bool passed = session.CabSetupInspected && session.CabPassedItems == CabSetupItem.All;
            if (passed)
            {
                GUILayout.Label("[ ✓ RECORDED ] Cab setup verified and within parameters.", readyStyle);
            }
            else
            {
                for (int index = 0; index < session.CabFindings.Count; index++)
                {
                    GUILayout.Label("- " + session.CabFindings[index], warningStyle);
                }
            }

            if (!passed && cab.CanRecord && cab.AllEquipmentReady)
            {
                GUILayout.Label("All live requirements are ready. Record the cab setup now.", readyStyle);
            }

            GUIStyle recordBtnStyle = passed ? paperButtonStyle : primaryActionButtonStyle;
            if (GUILayout.Button(passed ? "↺ Recheck Cab Setup & Certify" : "✓ Record & Certify Cab Setup Now", recordBtnStyle, GUILayout.Height(38f)))
            {
                bool result = session.InspectCabSetup(out string message);
                SetActionMessage(message, !result);
            }

            GUILayout.EndVertical();
        }

        private void DrawChargingStep()
        {
            GUILayout.Space(4f);
            GUILayout.Label("Charge and Prepare Brake System", sectionSubheaderStyle);
            GUILayout.Label("Set the automatic brake to release, run the compressor, connect hoses, set the angle "
                            + "cocks, and release the independent brake. Live pipe and cylinder values determine "
                            + "when the train is ready.", wrappedStyle);
            GUILayout.Label("Brake-pipe reach: " + session.ReachableNowCount() + " / " + session.CarCount
                            + " cars | control pipe: " + session.CurrentControlPipe.ToString("0.00") + " bar",
                ledgerKeyStyle);

            List<ContinuityBreak> breaks = session.GetLiveContinuityBreaks();
            for (int index = 0; index < breaks.Count; index++)
            {
                GUILayout.Label("Continuity open " + breaks[index].Description + ".", warningStyle);
            }

            if (InspectionTypeRules.RequiresLeakageTest(session.Type) && !session.LeakageCompleted)
            {
                string leakageReason;
                bool canBeginLeakage = session.CanBeginLeakage(out leakageReason);
                GUILayout.Label(canBeginLeakage
                        ? "The charged train is ready for its pressure-hold test."
                        : leakageReason,
                    canBeginLeakage ? readyStyle : warningStyle);
                GUI.enabled = canBeginLeakage;
                if (GUILayout.Button("⏱ Begin Leakage Hold (1-Minute Observation)", primaryActionButtonStyle, GUILayout.Height(38f)))
                {
                    session.BeginLeakage();
                    lastLoggedPhase = session.Phase;
                    SetActionMessage("Leakage hold started. Set the controlling train-brake CUTOUT control to CUT OUT.", false);
                }

                GUI.enabled = true;
            }
            else
            {
                if (session.LeakageCompleted)
                {
                    GUILayout.Label("Leakage hold complete | drop " + session.LeakageDrop.ToString("0.00")
                                    + " bar | limit " + Main.Settings.MaximumLeakageBar.ToString("0.00") + " bar",
                        session.LeakageDrop <= Main.Settings.MaximumLeakageBar ? readyStyle : failureStyle);
                }

                string applicationReason;
                bool canApply = session.CanBeginApplication(out applicationReason);
                GUILayout.Label(canApply
                        ? "The reachable segment is charged and ready for a service application."
                        : applicationReason,
                    canApply ? readyStyle : warningStyle);
                GUI.enabled = canApply;
                if (GUILayout.Button("✓ Record Charge & Begin Service Application", primaryActionButtonStyle, GUILayout.Height(38f)))
                {
                    session.BeginApplication();
                    lastLoggedPhase = session.Phase;
                    SetActionMessage(string.Empty, false);
                }

                GUI.enabled = true;
            }

            DrawCarReadings();
        }

        private void DrawLeakageStep()
        {
            GUILayout.Space(4f);
            GUILayout.Label("Brake-Pipe Leakage Hold Observation", sectionSubheaderStyle);
            GUILayout.Label("Set the controlling train-brake CUTOUT control to CUT OUT without changing hose or "
                            + "angle-cock connections. Leave that control at CUT OUT while the timer observes "
                            + "pipe-pressure loss. Do not move the automatic brake handle from RELEASE.",
                wrappedStyle);
            GUILayout.Label("Timer: " + session.LeakageElapsed.ToString("0.0") + " / "
                            + Main.Settings.LeakageHoldSeconds.ToString("0") + " s | pipe drop: "
                            + session.LeakageDrop.ToString("0.00") + " bar", ledgerKeyStyle);
            if (!session.LeakageCutoutObserved)
            {
                GUILayout.Label("Waiting for the controlling train-brake CUTOUT control to read CUT OUT.", warningStyle);
            }
            else if (session.LeakageDrop > Main.Settings.MaximumLeakageBar)
            {
                GUILayout.Label("The observed pressure loss exceeds the configured limit of "
                                + Main.Settings.MaximumLeakageBar.ToString("0.00") + " bar.", failureStyle);
            }

            string reason;
            bool canComplete = session.CanCompleteLeakage(out reason);
            GUILayout.Label(canComplete ? "The leakage observation is complete." : reason,
                canComplete ? readyStyle : warningStyle);
            GUI.enabled = canComplete;
            if (GUILayout.Button("✓ Record Leakage Result & Return to Charge", primaryActionButtonStyle, GUILayout.Height(38f)))
            {
                session.CompleteLeakage();
                lastLoggedPhase = session.Phase;
                SetActionMessage("Leakage result recorded. Set the controlling train-brake CUTOUT control back to CUT IN and recharge.",
                    session.LeakageDrop > Main.Settings.MaximumLeakageBar);
            }

            GUI.enabled = true;
            DrawCarReadings();
        }

        private void DrawApplicationStep()
        {
            GUILayout.Space(4f);
            GUILayout.Label("Apply the Train Brake (Service Reduction)", sectionSubheaderStyle);
            GUILayout.Label("Make a normal service reduction of at least "
                            + InspectionSession.MinimumServiceReduction.ToString("0.00")
                            + " bar and hold it. The inspector records every reachable cylinder response.",
                wrappedStyle);
            GUILayout.Label("Control pipe: " + session.ChargedControlPipe.ToString("0.00") + " -> "
                            + session.CurrentControlPipe.ToString("0.00") + " bar | reduction: "
                            + session.ServiceReduction.ToString("0.00") + " bar | responding: "
                            + session.RespondedCount() + " / " + session.TestedReachableCount(), ledgerKeyStyle);

            DrawRearApplicationConfirmation();

            string reason;
            bool canRelease = session.CanBeginRelease(out reason);
            GUILayout.Label(canRelease ? "The service reduction is recorded; the train brake may be released." : reason,
                canRelease ? readyStyle : warningStyle);
            GUI.enabled = canRelease;
            if (GUILayout.Button("✓ Record Application & Begin Release", primaryActionButtonStyle, GUILayout.Height(38f)))
            {
                session.BeginRelease();
                lastLoggedPhase = session.Phase;
                SetActionMessage(string.Empty, false);
            }

            GUI.enabled = true;
            DrawCarReadings();
        }

        private void DrawRearApplicationConfirmation()
        {
            if (!InspectionTypeRules.RequiresRearVerification(session.Type))
            {
                return;
            }

            CarInspectionRecord rear = session.RearRecord;
            if (session.RearApplicationConfirmed)
            {
                GUILayout.Label("[ ✓ ] Rear-car application physically confirmed on " + rear.Label + ".", readyStyle);
                return;
            }

            string reason;
            bool canConfirm = session.CanConfirmRearApplication(out reason);
            GUILayout.Label(canConfirm ? "You are in position to confirm the rear-car application." : reason,
                canConfirm ? readyStyle : warningStyle);
            GUI.enabled = canConfirm;
            if (GUILayout.Button("✓ Physically Confirm Rear-Car Brake Application", primaryActionButtonStyle, GUILayout.Height(34f)))
            {
                bool result = session.ConfirmRearApplication(out string message);
                SetActionMessage(message, !result);
            }

            GUI.enabled = true;
        }

        private void DrawReleaseStep()
        {
            GUILayout.Space(4f);
            GUILayout.Label("Release and Recharge Train Brakes", sectionSubheaderStyle);
            GUILayout.Label("Move the automatic brake fully to release and keep the compressor running while the "
                            + "pipe recharges. The inspector records each cylinder release.", wrappedStyle);
            GUILayout.Label("Control pipe: " + session.CurrentControlPipe.ToString("0.00") + " / target about "
                            + session.ChargedControlPipe.ToString("0.00") + " bar | released: "
                            + session.ReleasedCount() + " / " + session.TestedReachableCount(), ledgerKeyStyle);

            DrawRearReleaseConfirmation();

            string reason;
            bool canFinish = session.CanFinish(out reason);
            GUILayout.Label(canFinish ? "The brake pipe has recharged; finish to issue the official certification." : reason,
                canFinish ? readyStyle : warningStyle);
            GUI.enabled = canFinish;
            if (GUILayout.Button("★ Complete Inspection & Issue Certification", primaryActionButtonStyle, GUILayout.Height(40f)))
            {
                session.Finish();
                lastLoggedPhase = session.Phase;
                SetActionMessage(string.Empty, false);
            }

            GUI.enabled = true;
            DrawCarReadings();
        }

        private void DrawRearReleaseConfirmation()
        {
            if (!InspectionTypeRules.RequiresRearVerification(session.Type))
            {
                return;
            }

            CarInspectionRecord rear = session.RearRecord;
            if (session.RearReleaseConfirmed)
            {
                GUILayout.Label("[ ✓ ] Rear-car release physically confirmed on " + rear.Label + ".", readyStyle);
                return;
            }

            string reason;
            bool canConfirm = session.CanConfirmRearRelease(out reason);
            GUILayout.Label(canConfirm ? "You are in position to confirm the rear-car release." : reason,
                canConfirm ? readyStyle : warningStyle);
            GUI.enabled = canConfirm;
            if (GUILayout.Button("✓ Physically Confirm Rear-Car Brake Release", primaryActionButtonStyle, GUILayout.Height(34f)))
            {
                bool result = session.ConfirmRearRelease(out string message);
                SetActionMessage(message, !result);
            }

            GUI.enabled = true;
        }

        private void DrawRollingStep()
        {
            GUILayout.Space(4f);
            GUILayout.Label("Post-Departure Rolling Inspection", sectionSubheaderStyle);
            GUILayout.Label("With the train stopped after its first controlled movement, inspect both sides, brake "
                            + "rigging, wheels, and bearings. Complete the result after recording every car.",
                wrappedStyle);
            string reason;
            bool canFinish = session.CanFinish(out reason);
            GUI.enabled = canFinish;
            if (GUILayout.Button("★ Complete Rolling Inspection & Issue Certification", primaryActionButtonStyle, GUILayout.Height(40f)))
            {
                session.Finish();
                lastLoggedPhase = session.Phase;
                SetActionMessage(string.Empty, false);
            }

            GUI.enabled = true;
            if (!canFinish)
            {
                GUILayout.Label(reason, warningStyle);
            }

            DrawCarReadings();
        }

        private void DrawResult()
        {
            InspectionReport report = session.Report;
            if (report == null)
            {
                GUILayout.Label("The inspection result could not be generated.", failureStyle);
                return;
            }

            GUILayout.Space(6f);
            GUIStyle stampStyle = report.Passed ? stampCertificatePassedStyle : stampCertificateFailedStyle;
            GUILayout.BeginVertical(stampStyle);

            string stampHeader = report.Passed
                ? "★ DERAIL VALLEY RAILROAD — AIR BRAKE & EQUIPMENT CERTIFICATION ★"
                : "✖ MECHANICAL INSPECTION DEFECT NOTICE — BAD ORDER / FAILED ✖";
            GUILayout.Label(stampHeader, certTitleStyle);
            GUILayout.Space(4f);
            GUILayout.Label(report.Summary, certSummaryStyle);
            GUILayout.Space(4f);

            if (session.Certificate != null)
            {
                DateTime expires = new DateTime(session.Certificate.ExpiresUtcTicks, DateTimeKind.Utc).ToLocalTime();
                GUILayout.Label("OFFICIAL CERTIFICATE: " + session.Certificate.Id + "  |  EXPIRES: "
                                + expires.ToString("yyyy-MM-dd HH:mm") + " LOCAL TIME", certStatusStyle);
            }
            else if (!report.Passed)
            {
                GUILayout.Label("DEPARTURE PROHIBITED UNTIL ALL AIR BRAKE DEFECTS ARE REMEDIED", failureStyle);
            }

            GUILayout.Label("Recorded: " + report.CompletedAt.ToString("yyyy-MM-dd HH:mm:ss")
                            + " | Train Mass: " + report.TotalMassTonnes.ToString("0.0") + " t"
                            + (session.Type == InspectionType.Rolling
                                ? string.Empty
                                : " | Confirmed Operative Brake Mass: " + report.TestedBrakeMassPercent.ToString("0") + "%"),
                mutedStyle);

            GUILayout.EndVertical();

            if (report.Findings.Count > 0)
            {
                GUILayout.Space(4f);
                GUILayout.BeginVertical(innerCardStyle);
                GUILayout.Label("RECORDED MECHANICAL DEFECTS & FINDINGS", sectionSubheaderStyle);
                for (int index = 0; index < report.Findings.Count; index++)
                {
                    GUILayout.Label("• " + report.Findings[index], failureStyle);
                }
                GUILayout.EndVertical();
            }

            GUILayout.Space(4f);
            GUILayout.Label("Notice: Certification is strictly tied to this consist and controlling cab. Changing cars or locomotive requires re-inspection.", mutedStyle);
            DrawAllKnownFaults();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            bool again = GUILayout.Button("↺ Run This Inspection Again", paperButtonStyle, GUILayout.Height(36f));
            bool choose = GUILayout.Button("📋 Inspect Another Train / Return", paperButtonStyle, GUILayout.Height(36f));
            GUILayout.EndHorizontal();
            if (again)
            {
                RestartSession(session.IsReversed);
            }
            else if (choose)
            {
                ClearSession();
            }
            else
            {
                DrawCarReadings();
            }
        }

        private void DrawAllKnownFaults()
        {
            bool headingDrawn = false;
            for (int carIndex = 0; carIndex < session.Cars.Count; carIndex++)
            {
                CarInspectionRecord record = session.Cars[carIndex];
                List<MechanicalFaultData> faults = FaultRegistry.GetFaults(record.Car, false);
                if (faults.Count == 0)
                {
                    continue;
                }

                if (!headingDrawn)
                {
                    GUILayout.Label("Discovered mechanical defects", subheaderStyle);
                    headingDrawn = true;
                }

                DrawCarFaults(record);
            }
        }

        private void DrawInvalidated()
        {
            GUILayout.Space(8f);
            GUILayout.BeginVertical(stampCertificateFailedStyle);
            GUILayout.Label("✖ INSPECTION INVALIDATED ✖", certTitleStyle);
            GUILayout.Label(session.InvalidReason, certSummaryStyle);
            GUILayout.Label("A fresh inspection is required because train consist or cab configuration changed.", mutedStyle);
            GUILayout.EndVertical();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            Trainset current = session.GetCurrentTrainset();
            GUI.enabled = current != null && current.cars != null && current.cars.Count > 0;
            if (GUILayout.Button("Inspect Changed Consist", primaryActionButtonStyle, GUILayout.Height(38f)))
            {
                StartInspection(current, session.PreferredCar, session.IsReversed, session.Type);
            }

            GUI.enabled = true;
            if (GUILayout.Button("Choose Another Train", paperButtonStyle, GUILayout.Height(38f)))
            {
                ClearSession();
            }

            GUILayout.EndHorizontal();
        }

        private void DrawCarReadings()
        {
            GUILayout.Space(6f);
            GUILayout.BeginVertical(innerCardStyle);
            GUILayout.Label("CONSIST INDIVIDUAL UNIT READINGS", sectionSubheaderStyle);
            carScroll = GUILayout.BeginScrollView(carScroll, GUILayout.MinHeight(150f),
                GUILayout.MaxHeight(270f));
            for (int index = 0; index < session.Cars.Count; index++)
            {
                CarInspectionRecord record = session.Cars[index];
                string status = session.GetCarStatus(record, out CarStatusKind kind);
                GUILayout.Label("#" + record.Number.ToString("00") + " " + record.Id + " | " + status,
                    StyleFor(kind));
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private GUIStyle StyleFor(CarStatusKind kind)
        {
            switch (kind)
            {
                case CarStatusKind.Ready:
                    return readyStyle;
                case CarStatusKind.Warning:
                    return warningStyle;
                case CarStatusKind.Failure:
                    return failureStyle;
                case CarStatusKind.NotTested:
                    return notTestedStyle;
                default:
                    return wrappedStyle;
            }
        }

        private string PhaseProgressText()
        {
            if (session.Type == InspectionType.Rolling)
            {
                return session.Phase == InspectionPhase.Complete
                    ? "WALK-AROUND -> [RESULT]"
                    : "[WALK-AROUND] -> RESULT";
            }

            switch (session.Phase)
            {
                case InspectionPhase.Charging:
                    return InspectionTypeRules.RequiresLeakageTest(session.Type)
                        ? "[PREPARE] -> LEAKAGE -> APPLY -> RELEASE -> RESULT"
                        : "[PREPARE] -> APPLY -> RELEASE -> RESULT";
                case InspectionPhase.Leakage:
                    return "PREPARE -> [LEAKAGE] -> APPLY -> RELEASE -> RESULT";
                case InspectionPhase.Applying:
                    return "PREPARE -> [APPLY] -> RELEASE -> RESULT";
                case InspectionPhase.Releasing:
                    return "PREPARE -> APPLY -> [RELEASE] -> RESULT";
                case InspectionPhase.Complete:
                    return "PREPARE -> APPLY -> RELEASE -> [RESULT]";
                default:
                    return "INSPECTION INVALIDATED";
            }
        }

        private void StartInspection(Trainset trainset, TrainCar preferred, bool reverse, InspectionType type)
        {
            if (session != null)
            {
                session.Dispose();
            }

            viewingCertificate = null;
            session = InspectionSession.Create(trainset, preferred, reverse, type);
            selectedType = type;
            selectedCarIndex = 0;
            selectionScroll = Vector2.zero;
            walkaroundScroll = Vector2.zero;
            carScroll = Vector2.zero;
            mainScroll = Vector2.zero;
            actionMessage = string.Empty;
            if (session == null)
            {
                Main.ModEntry.Logger.Warning("Could not start an inspection for the selected trainset.");
                return;
            }

            CarInspectionRecord nextUnchecked = session.GetNextUncheckedRecord();
            if (nextUnchecked != null)
            {
                for (int i = 0; i < session.Cars.Count; i++)
                {
                    if (session.Cars[i] == nextUnchecked)
                    {
                        selectedCarIndex = i;
                        break;
                    }
                }
            }

            lastLoggedPhase = session.Phase;
            Main.ModEntry.Logger.Log("Started " + InspectionTypeRules.DisplayName(type).ToLowerInvariant()
                                     + " for a " + session.CarCount + "-car consist.");
        }

        private void RestartSession(bool reverse)
        {
            if (session == null)
            {
                return;
            }

            Trainset current = session.GetCurrentTrainset();
            TrainCar preferred = session.PreferredCar;
            InspectionType type = session.Type;
            if (current == null)
            {
                ClearSession();
            }
            else
            {
                StartInspection(current, preferred, reverse, type);
            }
        }

        private void ClearSession()
        {
            if (session != null)
            {
                session.Dispose();
                session = null;
            }

            viewingCertificate = null;
            InspectionSession.ClearActiveState();
            nearbyTrains.Clear();
            nextNearbyRefresh = 0f;
            selectedCarIndex = 0;
            selectionScroll = Vector2.zero;
            walkaroundScroll = Vector2.zero;
            carScroll = Vector2.zero;
            mainScroll = Vector2.zero;
            actionMessage = string.Empty;
        }

        private void SetActionMessage(string message, bool failure)
        {
            actionMessage = message ?? string.Empty;
            actionMessageIsFailure = failure;
        }

        private int IndexOfRecord(CarInspectionRecord record)
        {
            for (int index = 0; index < session.Cars.Count; index++)
            {
                if (ReferenceEquals(session.Cars[index], record))
                {
                    return index;
                }
            }

            return 0;
        }

        private void RefreshNearbyTrainsIfNeeded()
        {
            if (Time.unscaledTime < nextNearbyRefresh)
            {
                return;
            }

            nextNearbyRefresh = Time.unscaledTime + 1f;
            nearbyTrains.Clear();
            Transform player;
            try
            {
                player = PlayerManager.PlayerTransform;
            }
            catch
            {
                return;
            }

            if (player == null || Trainset.allSets == null)
            {
                return;
            }

            List<Trainset> trainsets;
            try
            {
                trainsets = new List<Trainset>(Trainset.allSets);
            }
            catch
            {
                return;
            }

            for (int setIndex = 0; setIndex < trainsets.Count; setIndex++)
            {
                Trainset trainset = trainsets[setIndex];
                if (trainset == null || trainset.cars == null || trainset.cars.Count == 0)
                {
                    continue;
                }

                float closestDistance = float.MaxValue;
                TrainCar closest = null;
                for (int carIndex = 0; carIndex < trainset.cars.Count; carIndex++)
                {
                    TrainCar car = trainset.cars[carIndex];
                    if (car == null)
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(player.position, car.transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closest = car;
                    }
                }

                if (closestDistance <= MaximumSelectionDistance && closest != null)
                {
                    nearbyTrains.Add(new NearbyTrain
                    {
                        Trainset = trainset,
                        PreferredCar = TrainIdentity.FindControllingCar(trainset, closest) ?? closest,
                        Distance = closestDistance
                    });
                }
            }

            nearbyTrains.Sort(delegate(NearbyTrain left, NearbyTrain right)
            {
                return left.Distance.CompareTo(right.Distance);
            });
            if (nearbyTrains.Count > 20)
            {
                nearbyTrains.RemoveRange(20, nearbyTrains.Count - 20);
            }
        }

        private static TrainCar SafePlayerCar()
        {
            try
            {
                return PlayerManager.Car;
            }
            catch
            {
                return null;
            }
        }

        private static string DescribeEndpoints(Trainset trainset)
        {
            TrainCar first = null;
            TrainCar last = null;
            for (int index = 0; index < trainset.cars.Count; index++)
            {
                TrainCar car = trainset.cars[index];
                if (car == null)
                {
                    continue;
                }

                if (first == null)
                {
                    first = car;
                }

                last = car;
            }

            return first == null
                ? "unknown consist"
                : TrainIdentity.SafeCarId(first) + " ... " + TrainIdentity.SafeCarId(last);
        }

        private CustomFirstPersonController GetPlayerController()
        {
            if (cachedPlayerController != null)
            {
                return cachedPlayerController;
            }

            try
            {
                Transform playerTransform = PlayerManager.PlayerTransform;
                if (playerTransform != null)
                {
                    cachedPlayerController = playerTransform.GetComponent<CustomFirstPersonController>();
                }
            }
            catch
            {
            }

            return cachedPlayerController;
        }

        private void SuspendPlayerControls()
        {
            CustomFirstPersonController controller = GetPlayerController();
            if (controller != null)
            {
                controller.FreeLookAllowed = false;
                if (controller.m_MouseLook != null)
                {
                    controller.m_MouseLook.RequestMouseSensitivityState(this, MouseSensitivityState.Locked, 100);
                }

                if (controller.Locomotion != null)
                {
                    controller.Locomotion.inputEnabled = InspectorInventoryItem.IsClipboardOpen;
                }
            }

            try
            {
                CursorManager.Instance?.RequestCursor(this, true, 100);
            }
            catch
            {
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            rightClickLookActive = false;
        }

        private void ResumePlayerControls()
        {
            CustomFirstPersonController controller = GetPlayerController();
            if (controller != null)
            {
                controller.FreeLookAllowed = true;
                if (controller.m_MouseLook != null)
                {
                    controller.m_MouseLook.RemoveRequest(this);
                }

                if (controller.Locomotion != null)
                {
                    controller.Locomotion.inputEnabled = true;
                }
            }

            try
            {
                CursorManager.Instance?.RemoveRequest(this);
            }
            catch
            {
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            rightClickLookActive = false;
        }

        private void CleanupGeneratedTextures()
        {
            for (int i = 0; i < generatedTextures.Count; i++)
            {
                if (generatedTextures[i] != null)
                {
                    Destroy(generatedTextures[i]);
                }
            }

            generatedTextures.Clear();
        }

        private Texture2D CreateBorderedTexture(int width, int height, Color fill, Color border, int borderWidth)
        {
            Color[] pix = new Color[width * height];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    bool isBorder = x < borderWidth || x >= width - borderWidth ||
                                    y < borderWidth || y >= height - borderWidth;
                    pix[y * width + x] = isBorder ? border : fill;
                }
            }

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels(pix);
            tex.Apply();
            tex.hideFlags = HideFlags.DontSave;
            generatedTextures.Add(tex);
            return tex;
        }

        private void ClampWindowToScreen()
        {
            windowRect.width = Mathf.Min(windowRect.width, Mathf.Max(470f, Screen.width - 10f));
            windowRect.height = Mathf.Min(windowRect.height, Mathf.Max(420f, Screen.height - 10f));
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height - windowRect.height));
        }

        private void EnsureStyles()
        {
            if (headerStyle != null)
            {
                return;
            }

            Texture2D texClipboard = CreateBorderedTexture(8, 8,
                new Color(0.18f, 0.20f, 0.23f, 0.98f), new Color(0.11f, 0.12f, 0.14f, 1f), 2);
            Texture2D texLetterhead = CreateBorderedTexture(8, 8,
                new Color(0.11f, 0.17f, 0.27f, 1f), new Color(0.72f, 0.60f, 0.35f, 1f), 1);
            Texture2D texCard = CreateBorderedTexture(8, 8,
                new Color(0.95f, 0.93f, 0.89f, 0.98f), new Color(0.48f, 0.50f, 0.54f, 1f), 1);
            Texture2D texInnerCard = CreateBorderedTexture(8, 8,
                new Color(0.98f, 0.97f, 0.95f, 0.98f), new Color(0.70f, 0.72f, 0.76f, 1f), 1);
            Texture2D texBtnNormal = CreateBorderedTexture(8, 8,
                new Color(0.91f, 0.89f, 0.84f, 1f), new Color(0.48f, 0.50f, 0.54f, 1f), 1);
            Texture2D texBtnHover = CreateBorderedTexture(8, 8,
                new Color(0.85f, 0.88f, 0.93f, 1f), new Color(0.25f, 0.35f, 0.50f, 1f), 1);
            Texture2D texBtnActive = CreateBorderedTexture(8, 8,
                new Color(0.78f, 0.82f, 0.88f, 1f), new Color(0.15f, 0.22f, 0.35f, 1f), 1);
            Texture2D texActionNormal = CreateBorderedTexture(8, 8,
                new Color(0.14f, 0.23f, 0.37f, 1f), new Color(0.75f, 0.62f, 0.35f, 1f), 1);
            Texture2D texActionHover = CreateBorderedTexture(8, 8,
                new Color(0.19f, 0.31f, 0.48f, 1f), new Color(0.90f, 0.78f, 0.45f, 1f), 1);
            Texture2D texActionActive = CreateBorderedTexture(8, 8,
                new Color(0.10f, 0.16f, 0.26f, 1f), new Color(0.60f, 0.50f, 0.28f, 1f), 1);
            Texture2D texDangerNormal = CreateBorderedTexture(8, 8,
                new Color(0.68f, 0.15f, 0.15f, 1f), new Color(0.45f, 0.08f, 0.08f, 1f), 1);
            Texture2D texDangerHover = CreateBorderedTexture(8, 8,
                new Color(0.78f, 0.20f, 0.20f, 1f), new Color(0.55f, 0.10f, 0.10f, 1f), 1);
            Texture2D texTabActive = CreateBorderedTexture(8, 8,
                new Color(0.14f, 0.23f, 0.37f, 1f), new Color(0.85f, 0.75f, 0.45f, 1f), 2);
            Texture2D texTabInactive = CreateBorderedTexture(8, 8,
                new Color(0.87f, 0.85f, 0.80f, 1f), new Color(0.55f, 0.57f, 0.60f, 1f), 1);
            Texture2D texCarDoneNormal = CreateBorderedTexture(8, 8,
                new Color(0.88f, 0.93f, 0.88f, 0.95f), new Color(0.20f, 0.52f, 0.28f, 1f), 1);
            Texture2D texCarDoneHover = CreateBorderedTexture(8, 8,
                new Color(0.82f, 0.89f, 0.83f, 1f), new Color(0.15f, 0.45f, 0.22f, 1f), 2);
            Texture2D texCarDoneSelected = CreateBorderedTexture(8, 8,
                new Color(0.20f, 0.38f, 0.26f, 1f), new Color(0.85f, 0.75f, 0.45f, 1f), 2);
            Texture2D texCertPass = CreateBorderedTexture(8, 8,
                new Color(0.91f, 0.96f, 0.92f, 1f), new Color(0.10f, 0.50f, 0.20f, 1f), 3);
            Texture2D texCertFail = CreateBorderedTexture(8, 8,
                new Color(0.97f, 0.91f, 0.91f, 1f), new Color(0.75f, 0.15f, 0.15f, 1f), 3);

            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = texClipboard;
            windowStyle.onNormal.background = texClipboard;
            windowStyle.border = new RectOffset(4, 4, 4, 4);
            windowStyle.padding = new RectOffset(10, 10, 22, 10);
            windowStyle.normal.textColor = new Color(0.92f, 0.90f, 0.85f);

            bannerBoxStyle = new GUIStyle(GUI.skin.box);
            bannerBoxStyle.normal.background = texLetterhead;
            bannerBoxStyle.border = new RectOffset(3, 3, 3, 3);
            bannerBoxStyle.padding = new RectOffset(12, 12, 8, 8);
            bannerBoxStyle.margin = new RectOffset(0, 0, 0, 6);

            letterheadTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            SetStyleColor(letterheadTitleStyle, new Color(0.96f, 0.94f, 0.88f));

            letterheadSubtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Normal,
                wordWrap = true
            };
            SetStyleColor(letterheadSubtitleStyle, new Color(0.80f, 0.85f, 0.92f));

            sheetCardStyle = new GUIStyle(GUI.skin.box);
            sheetCardStyle.normal.background = texCard;
            sheetCardStyle.border = new RectOffset(3, 3, 3, 3);
            sheetCardStyle.padding = new RectOffset(12, 12, 10, 10);
            sheetCardStyle.margin = new RectOffset(0, 0, 4, 6);

            innerCardStyle = new GUIStyle(GUI.skin.box);
            innerCardStyle.normal.background = texInnerCard;
            innerCardStyle.border = new RectOffset(2, 2, 2, 2);
            innerCardStyle.padding = new RectOffset(10, 10, 8, 8);
            innerCardStyle.margin = new RectOffset(0, 0, 3, 5);

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            SetStyleColor(headerStyle, new Color(0.08f, 0.14f, 0.24f));

            subheaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            SetStyleColor(subheaderStyle, new Color(0.12f, 0.18f, 0.28f));

            sectionHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            SetStyleColor(sectionHeaderStyle, new Color(0.08f, 0.14f, 0.24f));

            sectionSubheaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            SetStyleColor(sectionSubheaderStyle, new Color(0.18f, 0.22f, 0.28f));

            ledgerKeyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            SetStyleColor(ledgerKeyStyle, new Color(0.14f, 0.18f, 0.25f));

            wrappedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true
            };
            SetStyleColor(wrappedStyle, new Color(0.15f, 0.17f, 0.20f));

            mutedStyle = new GUIStyle(wrappedStyle);
            SetStyleColor(mutedStyle, new Color(0.40f, 0.44f, 0.48f));

            readyStyle = new GUIStyle(wrappedStyle) { fontStyle = FontStyle.Bold };
            SetStyleColor(readyStyle, new Color(0.08f, 0.48f, 0.18f));

            warningStyle = new GUIStyle(wrappedStyle) { fontStyle = FontStyle.Bold };
            SetStyleColor(warningStyle, new Color(0.70f, 0.38f, 0.05f));

            failureStyle = new GUIStyle(wrappedStyle) { fontStyle = FontStyle.Bold };
            SetStyleColor(failureStyle, new Color(0.75f, 0.12f, 0.12f));

            notTestedStyle = new GUIStyle(wrappedStyle);
            SetStyleColor(notTestedStyle, new Color(0.42f, 0.46f, 0.50f));

            paperButtonStyle = new GUIStyle(GUI.skin.button);
            paperButtonStyle.normal.background = texBtnNormal;
            paperButtonStyle.hover.background = texBtnHover;
            paperButtonStyle.active.background = texBtnActive;
            paperButtonStyle.border = new RectOffset(3, 3, 3, 3);
            paperButtonStyle.padding = new RectOffset(8, 8, 5, 5);
            paperButtonStyle.fontSize = 12;
            paperButtonStyle.fontStyle = FontStyle.Bold;
            SetStyleColor(paperButtonStyle, new Color(0.12f, 0.14f, 0.17f));

            primaryActionButtonStyle = new GUIStyle(paperButtonStyle);
            primaryActionButtonStyle.normal.background = texActionNormal;
            primaryActionButtonStyle.hover.background = texActionHover;
            primaryActionButtonStyle.active.background = texActionActive;
            primaryActionButtonStyle.fontSize = 13;
            SetStyleColor(primaryActionButtonStyle, new Color(0.96f, 0.94f, 0.88f));

            dangerButtonStyle = new GUIStyle(paperButtonStyle);
            dangerButtonStyle.normal.background = texDangerNormal;
            dangerButtonStyle.hover.background = texDangerHover;
            SetStyleColor(dangerButtonStyle, Color.white);

            checklistButtonStyle = new GUIStyle(paperButtonStyle);
            checklistButtonStyle.alignment = TextAnchor.MiddleLeft;
            checklistButtonStyle.padding = new RectOffset(10, 10, 6, 6);

            checklistButtonCheckedStyle = new GUIStyle(checklistButtonStyle);
            SetStyleColor(checklistButtonCheckedStyle, new Color(0.08f, 0.46f, 0.18f));

            checklistButtonDefectStyle = new GUIStyle(checklistButtonStyle);
            SetStyleColor(checklistButtonDefectStyle, new Color(0.75f, 0.12f, 0.12f));

            tabActiveStyle = new GUIStyle(GUI.skin.button);
            tabActiveStyle.normal.background = texTabActive;
            tabActiveStyle.hover.background = texTabActive;
            tabActiveStyle.border = new RectOffset(3, 3, 3, 3);
            tabActiveStyle.fontSize = 12;
            tabActiveStyle.fontStyle = FontStyle.Bold;
            SetStyleColor(tabActiveStyle, new Color(0.96f, 0.94f, 0.88f));

            tabInactiveStyle = new GUIStyle(GUI.skin.button);
            tabInactiveStyle.normal.background = texTabInactive;
            tabInactiveStyle.hover.background = texBtnHover;
            tabInactiveStyle.border = new RectOffset(3, 3, 3, 3);
            tabInactiveStyle.fontSize = 12;
            tabInactiveStyle.fontStyle = FontStyle.Normal;
            SetStyleColor(tabInactiveStyle, new Color(0.20f, 0.23f, 0.28f));

            carCompletedButtonStyle = new GUIStyle(paperButtonStyle);
            carCompletedButtonStyle.normal.background = texCarDoneNormal;
            carCompletedButtonStyle.hover.background = texCarDoneHover;
            carCompletedButtonStyle.fontSize = 12;
            carCompletedButtonStyle.fontStyle = FontStyle.Bold;
            SetStyleColor(carCompletedButtonStyle, new Color(0.08f, 0.42f, 0.18f));

            carCompletedSelectedButtonStyle = new GUIStyle(primaryActionButtonStyle);
            carCompletedSelectedButtonStyle.normal.background = texCarDoneSelected;
            carCompletedSelectedButtonStyle.hover.background = texCarDoneSelected;
            carCompletedSelectedButtonStyle.fontSize = 12;
            carCompletedSelectedButtonStyle.fontStyle = FontStyle.Bold;
            SetStyleColor(carCompletedSelectedButtonStyle, new Color(0.94f, 0.97f, 0.92f));

            carCompletedBannerStyle = new GUIStyle(GUI.skin.box);
            carCompletedBannerStyle.normal.background = texCertPass;
            carCompletedBannerStyle.border = new RectOffset(4, 4, 4, 4);
            carCompletedBannerStyle.padding = new RectOffset(12, 12, 8, 8);
            carCompletedBannerStyle.margin = new RectOffset(0, 0, 4, 4);

            stampCertificatePassedStyle = new GUIStyle(GUI.skin.box);
            stampCertificatePassedStyle.normal.background = texCertPass;
            stampCertificatePassedStyle.border = new RectOffset(4, 4, 4, 4);
            stampCertificatePassedStyle.padding = new RectOffset(16, 16, 12, 12);
            stampCertificatePassedStyle.margin = new RectOffset(0, 0, 6, 6);

            stampCertificateFailedStyle = new GUIStyle(stampCertificatePassedStyle);
            stampCertificateFailedStyle.normal.background = texCertFail;

            certTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            SetStyleColor(certTitleStyle, new Color(0.08f, 0.42f, 0.18f));

            certSummaryStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter
            };
            SetStyleColor(certSummaryStyle, new Color(0.10f, 0.14f, 0.18f));

            certStatusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            SetStyleColor(certStatusStyle, new Color(0.08f, 0.48f, 0.18f));

            passBoxStyle = new GUIStyle(stampCertificatePassedStyle);
            failBoxStyle = new GUIStyle(stampCertificateFailedStyle);
        }

        private static void SetStyleColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
        }
    }
}
