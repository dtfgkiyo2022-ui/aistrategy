using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rts.Presentation;
using Rts.Tactics;
using Rts.TacticsJs;
using UnityEngine;

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX
using Steamworks;
#endif

namespace Rts.Workshop
{
    /// <summary>Best-effort Steam Workshop bridge. Steam is optional and never part of the match simulation.</summary>
    public sealed class SteamWorkshopService : MonoBehaviour, IWorkshopControl
    {
        public const uint DevelopmentAppId = 480;
        private static readonly string[] VisibilityNames = { "非公開", "公開", "フレンドのみ", "限定公開" };
        private readonly List<string> subscribedFolders = new List<string>();
        private string status = "Steamにつながっていません";
        private string visibility = VisibilityNames[0];
        private bool initialized;
        private WorkshopPublicationMap publicationMap;
        private PendingPublication pending;

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX
        private CallResult<CreateItemResult_t> createItemResult;
        private CallResult<SubmitItemUpdateResult_t> submitItemResult;
        private Callback<DownloadItemResult_t> downloadItemResult;
        private UGCUpdateHandle_t updateHandle = UGCUpdateHandle_t.Invalid;
#endif

        private sealed class PendingPublication
        {
            public string SourceFolder;
            public WorkshopPreparationResult Preparation;
            public PublishedFileIdValue PublishedFile;
        }

        // A small wrapper keeps the service's public surface free of Steamworks types on unsupported platforms.
        private struct PublishedFileIdValue
        {
            public ulong Value;
            public bool IsValid { get { return Value != 0; } }
        }

        public bool SteamAvailable { get { return initialized; } }
        public string WorkshopStatus { get { return status; } }
        public string[] VisibilityChoices { get { return VisibilityNames; } }
        public string Visibility
        {
            get { return visibility; }
            set { if (VisibilityNames.Contains(value)) visibility = value; }
        }
        public IReadOnlyList<string> SubscribedTacticFolders { get { return subscribedFolders; } }
        public Action SubscribedTacticsChanged;

        private void Awake()
        {
            TryInitialize();
        }

        private void Update()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX
            if (!initialized) return;
            try
            {
                SteamAPI.RunCallbacks();
                if (updateHandle != UGCUpdateHandle_t.Invalid)
                {
                    ulong processed, total;
                    EItemUpdateStatus updateStatus = SteamUGC.GetItemUpdateProgress(updateHandle, out processed, out total);
                    int percent = total == 0 ? 0 : (int)Mathf.Clamp((float)(processed * 100UL / total), 0f, 100f);
                    status = "公開中 " + percent + "%（" + updateStatus + "）";
                }
            }
            catch (Exception e) { SetUnavailable(e); }
#endif
        }

        private void OnDestroy()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX
            if (downloadItemResult != null) downloadItemResult.Dispose();
            if (createItemResult != null) createItemResult.Dispose();
            if (submitItemResult != null) submitItemResult.Dispose();
            if (initialized)
            {
                try { SteamAPI.Shutdown(); } catch (Exception) { }
                initialized = false;
            }
#endif
        }

        public bool CanPublishTactic(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || string.Equals(folderPath, "なし", StringComparison.Ordinal)) return false;
            try
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string root = Path.GetFullPath(Path.Combine(documents, "AiCommandRts", "Tactics"));
                string path = Path.GetFullPath(folderPath);
                return path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is ArgumentException || e is IOException) { return false; }
        }

        public void PublishTactic(string folderPath)
        {
            if (!initialized) { status = "Steamにつながっていません"; return; }
            if (!CanPublishTactic(folderPath)) { status = "自分の戦術フォルダだけ公開できます"; return; }
            try
            {
                string runtimes = Path.Combine(UnityEngine.Application.streamingAssetsPath, "TacticRuntimes");
                var preparation = WorkshopTacticPreparer.Prepare(folderPath,
                    Path.Combine(UnityEngine.Application.temporaryCachePath, "Workshop"),
                    UnityEngine.Application.persistentDataPath,
                    folder =>
                    {
                        var loaded = TacticFolder.Load(folder, runtimes);
                        (loaded.Runtime as IDisposable)?.Dispose();
                        return loaded.IsSuccess ? null : loaded.Error;
                    });
                if (!preparation.IsSuccess) { status = "公開できません：" + preparation.Error; return; }
                publicationMap = WorkshopTacticPreparer.LoadPublicationMap(UnityEngine.Application.persistentDataPath);
                ulong existing;
                var publication = new PendingPublication { SourceFolder = folderPath, Preparation = preparation };
                if (publicationMap.TryGet(folderPath, out existing)) publication.PublishedFile = new PublishedFileIdValue { Value = existing };
                pending = publication;
                status = "公開準備中";
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX
                if (publication.PublishedFile.IsValid) StartItemUpdate(new PublishedFileId_t(publication.PublishedFile.Value));
                else
                {
                    SteamAPICall_t call = SteamUGC.CreateItem(new AppId_t(DevelopmentAppId), EWorkshopFileType.k_EWorkshopFileTypeCommunity);
                    if (call == SteamAPICall_t.Invalid) { status = "公開に失敗しました：CreateItem"; return; }
                    createItemResult.Set(call);
                }
#endif
            }
            catch (Exception e) { status = "公開に失敗しました：" + e.Message; }
        }

        public void RefreshWorkshopTactics()
        {
            if (!initialized) { status = "Steamにつながっていません"; return; }
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX
            try
            {
                subscribedFolders.Clear();
                uint count = SteamUGC.GetNumSubscribedItems();
                var ids = new PublishedFileId_t[Math.Max(1, (int)Math.Min(count, 100U))];
                uint actual = SteamUGC.GetSubscribedItems(ids, (uint)ids.Length);
                bool downloading = false;
                string runtimes = Path.Combine(UnityEngine.Application.streamingAssetsPath, "TacticRuntimes");
                for (int i = 0; i < actual; i++)
                {
                    PublishedFileId_t id = ids[i];
                    uint state = SteamUGC.GetItemState(id);
                    if ((state & (uint)EItemState.k_EItemStateInstalled) == 0)
                    {
                        if (SteamUGC.DownloadItem(id, false)) downloading = true;
                        continue;
                    }
                    ulong size; uint timestamp; string folder;
                    if (!SteamUGC.GetItemInstallInfo(id, out size, out folder, 4096, out timestamp) || string.IsNullOrWhiteSpace(folder)) continue;
                    var loaded = TacticFolder.Load(folder, runtimes);
                    (loaded.Runtime as IDisposable)?.Dispose();
                    if (loaded.IsSuccess) subscribedFolders.Add(folder);
                }
                status = downloading ? "購読した戦術をダウンロード中です" : "Workshopを読み直しました（" + subscribedFolders.Count + "件）";
                if (SubscribedTacticsChanged != null) SubscribedTacticsChanged();
            }
            catch (Exception e) { SetUnavailable(e); }
#endif
        }

        private void TryInitialize()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX
            try
            {
                initialized = SteamAPI.Init();
                status = initialized ? "Steamに接続しました" : "Steamにつながっていません";
                if (!initialized) return;
                createItemResult = CallResult<CreateItemResult_t>.Create(OnCreateItemResult);
                submitItemResult = CallResult<SubmitItemUpdateResult_t>.Create(OnSubmitItemUpdateResult);
                downloadItemResult = Callback<DownloadItemResult_t>.Create(OnDownloadItemResult);
            }
            catch (Exception e) { SetUnavailable(e); }
#else
            status = "Steamにつながっていません";
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX
        private void OnCreateItemResult(CreateItemResult_t result, bool ioFailure)
        {
            if (ioFailure || result.m_eResult != EResult.k_EResultOK || result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
            {
                status = result.m_bUserNeedsToAcceptWorkshopLegalAgreement ? "SteamのWorkshopの利用規約に同意が必要です" : "公開に失敗しました：" + result.m_eResult;
                return;
            }
            pending.PublishedFile = new PublishedFileIdValue { Value = (ulong)result.m_nPublishedFileId };
            publicationMap.Set(pending.SourceFolder, pending.PublishedFile.Value);
            publicationMap.Save();
            StartItemUpdate(result.m_nPublishedFileId);
        }

        private void StartItemUpdate(PublishedFileId_t id)
        {
            updateHandle = SteamUGC.StartItemUpdate(new AppId_t(DevelopmentAppId), id);
            if (updateHandle == UGCUpdateHandle_t.Invalid) { status = "公開に失敗しました：StartItemUpdate"; return; }
            var info = pending.Preparation.Info;
            if (!SteamUGC.SetItemTitle(updateHandle, info.Title) || !SteamUGC.SetItemDescription(updateHandle, info.Description)
                || !SteamUGC.SetItemTags(updateHandle, new List<string>(info.Tags)) || !SteamUGC.SetItemContent(updateHandle, pending.Preparation.StagedFolder)
                || !SteamUGC.SetItemVisibility(updateHandle, ToSteamVisibility(visibility)))
            { status = "公開に失敗しました：Workshop設定"; updateHandle = UGCUpdateHandle_t.Invalid; return; }
            SteamAPICall_t call = SteamUGC.SubmitItemUpdate(updateHandle, "戦術を更新");
            if (call == SteamAPICall_t.Invalid) { status = "公開に失敗しました：SubmitItemUpdate"; updateHandle = UGCUpdateHandle_t.Invalid; return; }
            submitItemResult.Set(call);
        }

        private void OnSubmitItemUpdateResult(SubmitItemUpdateResult_t result, bool ioFailure)
        {
            updateHandle = UGCUpdateHandle_t.Invalid;
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            { status = result.m_bUserNeedsToAcceptWorkshopLegalAgreement ? "SteamのWorkshopの利用規約に同意が必要です" : "公開に失敗しました：" + result.m_eResult; return; }
            status = "公開しました（" + visibility + "）";
            if (pending != null && publicationMap != null)
            {
                publicationMap.Set(pending.SourceFolder, (ulong)result.m_nPublishedFileId);
                publicationMap.Save();
            }
        }

        private void OnDownloadItemResult(DownloadItemResult_t result)
        {
            if (result.m_eResult == EResult.k_EResultOK) RefreshWorkshopTactics();
            else status = "Workshopのダウンロードに失敗しました：" + result.m_eResult;
        }

        private static ERemoteStoragePublishedFileVisibility ToSteamVisibility(string value)
        {
            switch (value)
            {
                case "公開": return ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic;
                case "フレンドのみ": return ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly;
                case "限定公開": return ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted;
                default: return ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate;
            }
        }
#endif

        private void SetUnavailable(Exception e)
        {
            initialized = false;
            status = "Steamにつながっていません";
            Debug.LogWarning("Steam Workshopは利用できません: " + e.Message);
        }
    }
}
