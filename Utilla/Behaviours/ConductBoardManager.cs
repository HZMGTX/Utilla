using BepInEx;
using BepInEx.Bootstrap;
using GorillaTag;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using Utilla.Attributes;
using Utilla.Tools;

namespace Utilla.Behaviours;

internal class ConductBoardManager : MonoBehaviour
{
    private int PageCount => boardContent.Count;

    private readonly List<Section> boardContent = [];

    private ModeSelectButton buttonTemplate;

    private GameObject stumpRootObject;

    private Transform conductTransform;

    private TextMeshPro baseHeaderText, baseBodyText, headerText, bodyText, footerText;

    private int currentPage = 0;

    public void Start()
    {
        buttonTemplate = FindFirstObjectByType<GameModeSelectorButtonLayout>().pf_button;

        stumpRootObject = Array.Find(ZoneManagement.instance.allObjects, gameObject => gameObject.name == "TreeRoom");
        conductTransform = stumpRootObject.transform.FindChildRecursive("code of conduct");

        baseHeaderText = stumpRootObject.transform.FindChildRecursive("CodeOfConductHeadingText")?.GetComponent<TextMeshPro>();
        if (baseHeaderText == null)
        {
            Logging.Warning("COC (Code of Conduct) header text is missing");
            return;
        }

        GameObject headingTextObject = Instantiate(baseHeaderText.gameObject);
        headingTextObject.transform.position = baseHeaderText.transform.position;
        headingTextObject.transform.rotation = baseHeaderText.transform.rotation;
        headingTextObject.transform.localScale = baseHeaderText.transform.localScale;
        SanitizeTextObject(headingTextObject);

        headerText = headingTextObject.GetComponent<TextMeshPro>();
        headerText.fontSizeMax = baseHeaderText.fontSize;
        headerText.enableAutoSizing = true;
        headerText.textWrappingMode = TextWrappingModes.NoWrap;

        GameObject footerTextObject = Instantiate(baseHeaderText.gameObject);
        footerTextObject.transform.position = baseHeaderText.transform.position;
        footerTextObject.transform.rotation = baseHeaderText.transform.rotation;
        footerTextObject.transform.localScale = baseHeaderText.transform.localScale;
        SanitizeTextObject(footerTextObject);

        footerText = footerTextObject.GetComponent<TextMeshPro>();
        footerText.text = $"{Constants.Name} {Constants.Version}".ToUpper();
        footerText.enableAutoSizing = false;
        footerText.fontSize = 45;
        footerText.margin = new Vector4(0f, 110f, 0f, 0f);
        footerText.characterSpacing = -11.5f;
        footerText.enabled = true;
        footerText.renderer.enabled = true;

        baseBodyText = stumpRootObject.transform.FindChildRecursive("COCBodyText")?.GetComponent<TextMeshPro>();
        if (baseBodyText == null)
        {
            Logging.Warning("COC (Code of Conduct) body text is missing");
            return;
        }

        GameObject bodyTextObject = Instantiate(baseBodyText.gameObject);
        bodyTextObject.transform.position = baseBodyText.transform.position;
        bodyTextObject.transform.rotation = baseBodyText.transform.rotation;
        bodyTextObject.transform.localScale = baseBodyText.transform.localScale;
        SanitizeTextObject(bodyTextObject);

        bodyText = bodyTextObject.GetComponent<TextMeshPro>();
        bodyText.fontSizeMax = baseBodyText.fontSize;
        bodyText.fontSizeMin = 0f;
        bodyText.enableAutoSizing = true;
        bodyText.margin = new Vector4(0f, 0f, 0f, 36f);
        bodyText.richText = true;

        boardContent.Insert(0, new());

        CreateConductButton(-1f, "-->", NextPage);
        CreateConductButton(1f, "<--", PrevPage);

        ShowPage();
        CheckVersion();
        CreateEntries();
    }

    private void NextPage()
    {
        currentPage = (currentPage + 1) % PageCount;
        ShowPage();
    }

    private void PrevPage()
    {
        currentPage = (currentPage <= 0) ? PageCount - 1 : currentPage - 1;
        ShowPage();
    }

    private void ShowPage()
    {
        if (baseHeaderText == null || baseBodyText == null) return;

        Section content = boardContent.ElementAtOrDefault(Mathf.Max(0, Mathf.Min(currentPage, boardContent.Count - 1)));

        if (content.UseBaseText)
        {
            baseHeaderText.renderer?.forceRenderingOff = false;
            headerText.enabled = false;

            baseBodyText.renderer?.forceRenderingOff = false;
            bodyText.enabled = false;
        }
        else
        {
            baseHeaderText.renderer?.forceRenderingOff = true;
            headerText.enabled = true;
            headerText.text = content.Title;

            baseBodyText.renderer?.forceRenderingOff = true;
            bodyText.enabled = true;
            bodyText.text = content.Body;
        }
    }

    private void CreateConductButton(float horizontalPosition, string text, Action onButtonPressed = null)
    {
        GameObject buttonObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        buttonObject.name = $"UtillaButton_{text}";
        buttonObject.transform.parent = conductTransform;
        buttonObject.transform.localPosition = new Vector3(horizontalPosition, 0.52f, 0.13f);
        buttonObject.transform.localRotation = Quaternion.Euler(353.5f, 0f, 0f);
        buttonObject.transform.localScale = new Vector3(0.1427168f, 0.1427168f, 0.1f);
        buttonObject.GetComponent<Renderer>().material = buttonTemplate.unpressedMaterial;
        buttonObject.GetComponent<Collider>().isTrigger = true;
        buttonObject.SetLayer(UnityLayer.GorillaInteractable);

        GameObject textObject = new();
        textObject.transform.parent = buttonObject.transform;
        textObject.transform.localPosition = Vector3.forward * 0.525f;
        textObject.transform.localRotation = Quaternion.AngleAxis(180f, Vector3.up);
        textObject.transform.localScale = Vector3.one;

        TextMeshPro textMeshPro = textObject.AddComponent<TextMeshPro>();
        textMeshPro.font = buttonTemplate?.GetComponentInChildren<TMP_Text>()?.font ?? stumpRootObject.GetComponentInChildren<GorillaComputerTerminal>()?.myScreenText?.font;
        textMeshPro.alignment = TextAlignmentOptions.Center;
        textMeshPro.characterSpacing = -10f;
        textMeshPro.overflowMode = TextOverflowModes.Overflow;
        textMeshPro.fontSize = 3f;
        textMeshPro.color = new Color(0.1960784f, 0.1960784f, 0.1960784f);
        textMeshPro.text = text;

        GorillaPressableButton pressableButton = buttonObject.AddComponent<GorillaPressableButton>();
        pressableButton.buttonRenderer = buttonObject.GetComponent<MeshRenderer>();
        pressableButton.unpressedMaterial = buttonTemplate.unpressedMaterial;
        pressableButton.pressedMaterial = buttonTemplate.pressedMaterial;

        UnityEvent onPressEvent = new();
        onPressEvent.AddListener(new UnityAction(() =>
        {
            pressableButton.StartCoroutine(ButtonColourUpdate(pressableButton));
        }));
        onPressEvent.AddListener(new UnityAction(onButtonPressed));
        pressableButton.onPressButton = onPressEvent;
    }

    public async void CheckVersion()
    {
        string api = "https://api.github.com/repos/HZMGTX/Utilla/releases/latest";
        UnityWebRequest webRequest = UnityWebRequest.Get(api);

        UnityWebRequestAsyncOperation asyncOperation = webRequest.SendWebRequest();
        await asyncOperation;

        if (webRequest.result != UnityWebRequest.Result.Success)
        {
            Logging.Fatal($"Version could not be accessed from {webRequest.url}");
            Logging.Info(webRequest.error);
            return;
        }

        try
        {
            JObject json = JObject.Parse(webRequest.downloadHandler.text);
            string latestTag = json["tag_name"]?.ToString();

            if (string.IsNullOrEmpty(latestTag))
                return;

            if (latestTag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                latestTag = latestTag.Substring(1);

            if (Version.TryParse(Constants.Version, out Version installedVersion) &&
                Version.TryParse(latestTag, out Version latestVersion) &&
                latestVersion > installedVersion)
            {
                Logging.Message($"A new version of Utilla is available: {latestVersion} (installed version: {installedVersion})");

                footerText.color = Color.red;
                footerText.fontSize *= 0.85f;
                footerText.text = $"{Constants.Name} {Constants.Version} - please update to {latestVersion}".ToUpper();

                PromptSeralythMenu($"From <b>Utilla</b>:\nA new version of Utilla is available ({latestVersion})! Would you like to update to the latest version?", Update);
            }
        }
        catch (Exception e)
        {
            Logging.Fatal($"Failed to get latest version from GitHub: {e.Message}");
        }
    }

    private void PromptSeralythMenu(string message, Action accept = null, Action decline = null, string acceptButton = "Yes", string declineButton = "No")
    {
        try
        {
            const string SeralythMenuGuid = "org.seralyth.gorillatag.seralythmenu";

            if (!Chainloader.PluginInfos.TryGetValue(SeralythMenuGuid, out var pluginInfo) || pluginInfo?.Instance == null)
            {
                Logging.Warning("Seralyth Menu is not installed");
                return;
            }

            var assembly = pluginInfo.Instance.GetType().Assembly;
            MethodInfo promptMethod = null;

            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    Logging.Log(method.Name);
                    if (method.Name == "Prompt")
                    {
                        promptMethod = method;
                        break;
                    }
                }

                if (promptMethod != null)
                    break;
            }

            if (promptMethod != null)
            {
                promptMethod.Invoke(null,
                [
                    message,
                    accept,
                    decline,
                    acceptButton,
                    declineButton
                ]);
            }
            else
            {
                Logging.Warning("Seralyth Menu does not have a Prompt method");
            }
        }
        catch (Exception ex)
        {
            Logging.Error($"Failed to spawn prompt on Seralyth Menu: {ex}");
        }
    }

    public static void Update()
    {
        switch (SystemInfo.operatingSystemFamily)
        {
            case OperatingSystemFamily.Windows:
                {
                    string updateScript = @"@echo off
                    title Utilla Updater

                    echo Utilla is updating, please wait...
                    echo.

                    set ""PLUGIN_PATH=BepInEx\plugins""

                    for %%F in (""%PLUGIN_PATH%\*utilla*.dll"") do (
                        set ""UTILLA_FILE=%%F""
                        goto update
                    )

                    echo No update file found, skipping update.
                    goto restart

                    :update
                    echo Downloading the latest release of Utilla...

                    curl -L -o ""%UTILLA_FILE%"" ^
                    ""https://github.com/HZMGTX/Utilla/releases/latest/download/Utilla.dll""

                    goto restart
                      
                    :restart

                    :WAIT_LOOP
                    tasklist /FI ""IMAGENAME eq Gorilla Tag.exe"" | find /I ""Gorilla Tag.exe"" >nul
                    if %ERRORLEVEL%==0 (
                        timeout /t 1 >nul
                        goto WAIT_LOOP
                    )

                    echo Launching Gorilla Tag...
                    start steam://run/1533390
                    exit";

                    string fileName = "UpdateScript.bat";

                    File.WriteAllText(fileName, updateScript);

                    string filePath = Assembly.GetExecutingAssembly().Location.Replace("\\", "/").Split("/BepInEx")[0] + "/" + fileName;
                    Process.Start(filePath);
                    Application.Quit();
                    break;
                }
            case OperatingSystemFamily.Linux:
                {
                    string updateScript = @"#!/bin/bash
                    clear
                    echo ""Utilla is updating, please wait...""
                    echo

                    PLUGIN_PATH=""BepInEx/plugins""
                    MENU_FILE=""""

                    
                    for f in ""$PLUGIN_PATH""/*utilla*.dll; do
                        if [ -f ""$f"" ]; then
                            MENU_FILE=""$f""
                            break
                        fi
                    done

                    if [ -z ""$MENU_FILE"" ]; then
                        echo ""No menu file found, skipping update.""
                    else
                        echo ""Downloading latest release of Seralyth Menu...""
                        curl -L -o ""$MENU_FILE"" \
                        ""https://github.com/HZMGTX/Utilla/releases/latest/download/Utilla.dll""
                    fi

                    while pgrep -f ""GorillaTag.exe"" > /dev/null; do
                        sleep 1
                    done

                    echo ""Launching Gorilla Tag...""
                    xdg-open ""steam://run/1533390""
                    exit 0";

                    string fileName = $"UpdateScript.sh";
                    File.WriteAllText(fileName, updateScript);
                    Process.Start("chmod", $"+x \"{fileName}\"");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"\"{fileName}\"",
                        UseShellExecute = false
                    });
                    Application.Quit();
                    break;
                }
        }
    }

    private async void CreateEntries()
    {
        await DownloadEntries();

        foreach (var info in Chainloader.PluginInfos)
        {
            if (info.Value is null) continue;

            BaseUnityPlugin plugin = info.Value.Instance;
            if (plugin is null) continue;

            Type type = plugin.GetType();
            IEnumerable<ModdedBoardTextAttribute> attributes = type.GetCustomAttributes<ModdedBoardTextAttribute>();

            if (attributes is not null)
            {
                var assembly = type.Assembly;
                var names = assembly.GetManifestResourceNames();

                foreach (ModdedBoardTextAttribute attribute in attributes)
                {
                    if (string.IsNullOrEmpty(attribute.Title) || string.IsNullOrWhiteSpace(attribute.Title) || string.IsNullOrEmpty(attribute.Text) || string.IsNullOrWhiteSpace(attribute.Text)) continue;

                    if (names.SingleOrDefault(resourceName => resourceName == attribute.Text) is string resourceName)
                    {
                        using Stream stream = assembly.GetManifestResourceStream(resourceName);
                        using StreamReader reader = new(stream);
                        string resourceText = reader.ReadToEnd();
                        boardContent.Add(new(attribute.Title, resourceText));
                        continue;
                    }

                    boardContent.Add(new(attribute.Title, attribute.Text));
                }
            }
        }
    }

    public async Task DownloadEntries()
    {
        using UnityWebRequest webRequest = UnityWebRequest.Get(string.Join('/', Constants.InfoRepositoryURL, "ConductBoard", "Entries.json"));
        UnityWebRequestAsyncOperation asyncOperation = webRequest.SendWebRequest();
        await asyncOperation;

        if (webRequest.result == UnityWebRequest.Result.Success)
        {
            foreach (JObject item in JArray.Parse(webRequest.downloadHandler.text).Cast<JObject>())
            {
                Logging.Message(item.ToString(Formatting.Indented));

                using UnityWebRequest webRequest2 = UnityWebRequest.Get(string.Join('/', Constants.InfoRepositoryURL, (string)item.Property("body").Value));
                asyncOperation = webRequest2.SendWebRequest();
                await asyncOperation;

                if (webRequest2.result != UnityWebRequest.Result.Success)
                {
                    Logging.Fatal($"Body text could not be accessed from {webRequest2.url}");
                    Logging.Error(webRequest.downloadHandler.error);
                    continue;
                }

                boardContent.Add(new((string)item.Property("title").Value, webRequest2.downloadHandler.text));
            }
        }
        else
        {
            Logging.Fatal($"ModData could not be accessed from {webRequest.url}");
            Logging.Info(webRequest.downloadHandler.error);
        }
    }

    private IEnumerator ButtonColourUpdate(GorillaPressableButton pressableButton)
    {
        pressableButton.isOn = true;
        pressableButton.UpdateColor();

        yield return new WaitForSeconds(pressableButton.debounceTime);
        if ((pressableButton.touchTime + pressableButton.debounceTime) < Time.time)
        {
            pressableButton.isOn = false;
            pressableButton.UpdateColor();
        }

        yield break;
    }

    private void SanitizeTextObject(GameObject gameObject)
    {
        Type[] typesToRemove = [typeof(PlayFabTitleDataTextDisplay), typeof(LocalizedText), typeof(StaticLodGroup)];
        Component[] components = gameObject.GetComponents<Component>();

        for (int i = 0; i < components.Length; i++)
        {
            Type type = components[i].GetType();
            if (typesToRemove.Contains(type)) Destroy(components[i]);
        }
    }

    private struct Section
    {
        public bool UseBaseText;

        [TextArea(1, 1)]
        public string Title;

        [TextArea(12, 32)]
        public string Body;

        public Section()
        {
            UseBaseText = true;
        }

        public Section(string title, string body)
        {
            UseBaseText = false;
            Title = title;
            Body = body;
        }
    }
}
