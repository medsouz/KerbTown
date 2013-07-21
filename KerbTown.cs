﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace Kerbtown
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KerbTown : MonoBehaviour
    {
        private string _currentBodyName = "";
        private CelestialObject _currentCelestialObj;
        private string _currentConfigUrl = "";
        private string _currentModelUrl = "";
        private StaticObject _currentSelectedObject;

        private Dictionary<string, List<StaticObject>> _instancedList; // Initialized OnStart()
        private Dictionary<string, string> _modelList;

        private PQSCity.LODRange _myLodRange;
        private float _prevRotationAngle;

        #region Window Properties

        #region Window Rects

        private Rect _assetListRect = new Rect(460, 40, 600, 370);
        private Rect _currAssetListRect = new Rect(480, 60, 600, 370);
        private Rect _mainWindowRect = new Rect(20, 20, 410, 400);

        #endregion

        #region Visibility Flags

        private bool _availAssetsVisible = true;
        private bool _currAssetsVisible;
        private bool _mainWindowVisible = false;
        private bool _selectedWindowVisible = true;

        #endregion

        private Vector2 _availAssetScrollPos;
        private Vector2 _currAssetScrollPos;
        private string _currentObjectID = "";

        #endregion

        private void Start()
        {
            GenerateModelLists();
            InstantiateStaticsFromInstanceList();

            _currentBodyName = FlightGlobals.currentMainBody.bodyName;
            GameEvents.onDominantBodyChange.Add(BodyChangedCallback);
            GameEvents.onFlightReady.Add(FlightReadyCallBack);
        }

        // Load
        private void InstantiateStaticsFromInstanceList()
        {
            Stopwatch stopWatch = Stopwatch.StartNew();

            foreach (StaticObject instance in _instancedList.Keys.SelectMany(instList => _instancedList[instList]))
            {
                InstantiateStatic((_currentCelestialObj = GetCelestialObject(instance.CelestialBodyName)).PQSComponent,
                    instance);
            }

            stopWatch.Stop();
            Extensions.LogInfo(string.Format("Loaded static objects. ({0}ms)", stopWatch.ElapsedMilliseconds));
        }

        // Save
        private void WriteSessionConfigs()
        {
            Stopwatch stopWatch = Stopwatch.StartNew();
            ConfigNode modelPartRootNode = null;

            foreach (string instList in _instancedList.Keys)
            {
                var staticNode = new ConfigNode("STATIC");
                string modelPhysPath = "";
                bool nodesCleared = false;

                foreach (StaticObject inst in _instancedList[instList])
                {
                    if (!nodesCleared)
                    {
                        // Assign the root node for this static part.
                        modelPartRootNode = GameDatabase.Instance.GetConfigNode(inst.ConfigURL);

                        // Assign physical path to object config.
                        modelPhysPath = inst.ConfigURL.Substring(0, inst.ConfigURL.LastIndexOf('/')) + ".cfg";

                        // Remove existing nodes.
                        modelPartRootNode.RemoveNodes("Instances");

                        // Skip this until next static part.
                        nodesCleared = true;
                    }

                    var instanceNode = new ConfigNode("Instances");

                    instanceNode.AddValue("RadialPosition", ConfigNode.WriteVector(inst.RadPosition));
                    instanceNode.AddValue("RotationAngle", inst.RotAngle.ToString(CultureInfo.InvariantCulture));
                    instanceNode.AddValue("RadiusOffset", inst.RadOffset.ToString(CultureInfo.InvariantCulture));
                    instanceNode.AddValue("Orientation", ConfigNode.WriteVector(inst.Orientation));
                    instanceNode.AddValue("VisibilityRange", inst.VisRange.ToString(CultureInfo.InvariantCulture));
                    instanceNode.AddValue("CelestialBody", inst.CelestialBodyName);

                    modelPartRootNode.nodes.Add(instanceNode);
                }

                // No current instances - find the config url that is paired with the model url.

                if (_instancedList[instList].Count == 0)
                {
                    modelPartRootNode = GameDatabase.Instance.GetConfigNode(_modelList[instList]);
                    modelPhysPath = _modelList[instList].Substring(0, _modelList[instList].LastIndexOf('/')) + ".cfg";
                    modelPartRootNode.RemoveNodes("Instances");
                }
             
                staticNode.AddNode(modelPartRootNode);
                staticNode.Save(KSPUtil.ApplicationRootPath + "GameData/" + modelPhysPath,
                    " Generated by KerbTown - Hubs' Electrical");
            }

            stopWatch.Stop();
            Extensions.LogInfo(string.Format("Saved static objects. ({0}ms)", stopWatch.ElapsedMilliseconds));
        }

        private void FlightReadyCallBack()
        {
            _currentBodyName = FlightGlobals.currentMainBody.bodyName;
        }

        private void BodyChangedCallback(GameEvents.FromToAction<CelestialBody, CelestialBody> data)
        {
            _currentBodyName = data.to.bodyName;
        }

        private void Update()
        {
            // CTRL + K for show/hide.
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                if (Input.GetKeyDown(KeyCode.K))
                {
                    _mainWindowVisible = !_mainWindowVisible;
                }
            }
        }

        private static Vector3 GetLocalPosition(CelestialBody celestialObject, double latitude, double longitude)
        {
            return Vector3.zero;
        }

        private static double GetLongitude(CelestialObject celestialObject, Vector3d radialPosition)
        {
            if (celestialObject != null)
            {
                return celestialObject.CelestialBodyComponent.GetLongitude(
                    celestialObject.PQSComponent.GetWorldPosition(radialPosition));
            }
            Extensions.LogWarning("Celestial Object is null. - GetLongitude");
            return 0.0;
        }

        private static double GetLatitude(CelestialObject celestialObject, Vector3d radialPosition)
        {
            if (celestialObject != null)
            {
                return celestialObject.CelestialBodyComponent.GetLatitude(
                    celestialObject.PQSComponent.GetWorldPosition(radialPosition));
            }
            Extensions.LogWarning("Celestial Object is null. - GetLatitude");
            return 0.0;
        }

        private void GenerateModelLists()
        {
            UrlDir.UrlConfig[] staticConfigs = GameDatabase.Instance.GetConfigs("STATIC");
            _instancedList = new Dictionary<string, List<StaticObject>>();
            _modelList = new Dictionary<string, string>();

            foreach (UrlDir.UrlConfig staticUrlConfig in staticConfigs)
            {
                string model = staticUrlConfig.config.GetValue("mesh");
                if (string.IsNullOrEmpty(model))
                {
                    Extensions.LogError("Missing 'mesh' parameter for " + staticUrlConfig.url);
                    continue;
                }

                model = model.Substring(0, model.LastIndexOf('.'));
                string modelUrl = staticUrlConfig.url.Substring(0, staticUrlConfig.url.SecondLastIndex('/')) + "/" +
                                  model;

                Extensions.LogWarning("Model url: " + modelUrl);
                Extensions.LogWarning("Config url: " + staticUrlConfig.url);
                _modelList.Add(modelUrl, staticUrlConfig.url);

                // If we already have previous instances of the object, fill up the lists so that KerbTown can start instantiating them
                if (!staticUrlConfig.config.HasNode("Instances"))
                    continue;

                foreach (ConfigNode ins in staticUrlConfig.config.GetNodes("Instances"))
                {
                    Vector3 radPosition = ConfigNode.ParseVector3(ins.GetValue("RadialPosition"));
                    float rotAngle = float.Parse(ins.GetValue("RotationAngle"));
                    float radOffset = float.Parse(ins.GetValue("RadiusOffset"));
                    Vector3 orientation = ConfigNode.ParseVector3(ins.GetValue("Orientation"));
                    float visRange = float.Parse(ins.GetValue("VisibilityRange"));
                    string celestialBodyName = ins.GetValue("CelestialBody");

                    if (_instancedList.ContainsKey(modelUrl))
                    {
                        _instancedList[modelUrl].Add(
                            new StaticObject(radPosition, rotAngle, radOffset, orientation,
                                visRange, modelUrl, staticUrlConfig.url, celestialBodyName));
                    }
                    else
                    {
                        _instancedList.Add(modelUrl,
                            new List<StaticObject>
                            {
                                new StaticObject(radPosition, rotAngle, radOffset, orientation,
                                    visRange, modelUrl, staticUrlConfig.url, celestialBodyName)
                            });
                    }
                }
            }
        }

        private void InstantiateStatic(PQS celestialPQS, StaticObject sObject)
        {
            float visibilityRange = sObject.VisRange;
            Vector3 orientDirection = sObject.Orientation;
            float localRotationAngle = sObject.RotAngle;
            float radiusOffset = sObject.RadOffset;
            Vector3 radialPosition = sObject.RadPosition;
            string modelUrl = sObject.ModelUrl;

            if (radialPosition == Vector3.zero)
            {
                // Bug
                // Translates only when flight scene is reloaded. ActiveVessel.GetWorldPos3D() returns incorrect reference details.
                radialPosition = celestialPQS.GetRelativePosition(FlightGlobals.ActiveVessel.GetWorldPos3D());
                sObject.RadPosition = radialPosition;
            }

            if (orientDirection == Vector3.zero)
            {
                orientDirection = Vector3.up;
                sObject.Orientation = orientDirection;
            }

            sObject.Latitude = GetLatitude(_currentCelestialObj, radialPosition);
            sObject.Longitude = GetLongitude(_currentCelestialObj, radialPosition);

            GameObject staticGameObject = GameDatabase.Instance.GetModel(modelUrl);

            // Set objects to layer 15 so that they collide correctly with Kerbals.
            SetLayerRecursively(staticGameObject, 15);

            // Set the parent object to the celestial component's GameObject.
            staticGameObject.transform.parent = celestialPQS.transform;

            // Added not for collision support but to reduce performance cost when moving static objects around.
            if (staticGameObject.GetComponent<Rigidbody>() == null)
            {
                var rigidBody = staticGameObject.AddComponent<Rigidbody>();
                rigidBody.useGravity = false;   // Todo remove redundant code and test.
                rigidBody.isKinematic = true;
            }
            
            _myLodRange = new PQSCity.LODRange
                          {
                              renderers = new[] {staticGameObject},
                              objects = new GameObject[0],
                              isActive = true,
                              visibleRange = visibilityRange
                          };

            var myCity = staticGameObject.AddComponent<PQSCity>();

            myCity.lod = new[] {_myLodRange};
            myCity.frameDelta = 1;
            myCity.repositionToSphere = true;
            myCity.repositionRadial = radialPosition;
            myCity.repositionRadiusOffset = radiusOffset;
            myCity.reorientFinalAngle = localRotationAngle;
            myCity.reorientToSphere = true;
            myCity.reorientInitialUp = orientDirection;
            myCity.sphere = celestialPQS;

            myCity.order = 100;

            myCity.OnSetup();
            myCity.Orientate();

            staticGameObject.SetActive(true);
            
            sObject.PQSCityComponent = myCity;            
            sObject.StaticGameObject = staticGameObject;

            //AddModuleComponents(staticGameObject);
        }

        private static void SetLayerRecursively(GameObject staticGameObject, int newLayerNumber)
        {
            // Only set to layer 15 if the collider is not a trigger.
            if ((staticGameObject.collider != null && staticGameObject.collider.isTrigger) ||
                staticGameObject.collider == null)
            {
                staticGameObject.layer = newLayerNumber;
            }

            foreach (Transform child in staticGameObject.transform)
            {
                SetLayerRecursively(child.gameObject, newLayerNumber);
            }
        }

        private static CelestialObject GetCelestialObject(string celestialName)
        {
            return (from GameObject gameObjectInScene in FindSceneObjectsOfType(typeof (GameObject))
                from child in gameObjectInScene.GetComponentsInChildren<PQS>()
                where child.name == celestialName
                select new CelestialObject(gameObjectInScene)).FirstOrDefault();
        }

        private StaticObject GetStaticObjectFromID(string objectID)
        {
            return _instancedList[_currentModelUrl].FirstOrDefault(obFind => obFind.ObjectID == objectID);
        }

        private void RemoveCurrentStaticObject(string modelURL)
        {
            _instancedList[modelURL].Remove(_currentSelectedObject);
        }

        private StaticObject GetDefaultStaticObject(string modelUrl, string configUrl)
        {
            return new StaticObject(Vector3.zero, 0, GetSurfaceRadiusOffset(), Vector3.up, 1000, modelUrl, configUrl, "");
        }

        private float GetSurfaceRadiusOffset()
        {
            //todo change to activevessel.altitude after further investigation or just to surface height..

            Vector3d relativePosition =
                _currentCelestialObj.PQSComponent.GetRelativePosition(FlightGlobals.ActiveVessel.GetWorldPos3D());
            Vector3d rpNormalized = relativePosition.normalized;

            return (float) (relativePosition.x/rpNormalized.x - _currentCelestialObj.PQSComponent.radius);
        }

        #region GUI

        private void OnGUI()
        {
            if (!_mainWindowVisible)
                return;

            _mainWindowRect = GUI.Window(0x8100, _mainWindowRect, DrawMainWindow, "KerbTown Editor");


            if (_availAssetsVisible)
                _assetListRect = GUI.Window(0x8101, _assetListRect, DrawAvailAssetWindow, "Available Static Assets List");

            if (_currAssetsVisible)
            {
                _currAssetListRect = GUI.Window(0x8102, _currAssetListRect, DrawCurrAssetWindow,
                    "Existing Static Assets List");
            }

            if (_selectedWindowVisible)
            {
                GUI.Window(0x8103,
                    new Rect(_mainWindowRect.x + 5, _mainWindowRect.y + _mainWindowRect.height + 5, 400,
                        _currentSelectedObject != null ? 150 : 50), DrawSelectedWindow, "Selected Object Information");
            }
        }

        private void DrawMainWindow(int windowID)
        {
            GUI.Label(new Rect(10, 20, 100, 22), "Asset Lists");

            if (GUI.Button(new Rect(20, 40, 180, 22), _availAssetsVisible ? "Hide Available" : "Show Available"))
                _availAssetsVisible = !_availAssetsVisible;

            if (GUI.Button(new Rect(210, 40, 180, 22), _currAssetsVisible ? "Hide Existing" : "Show Existing"))
                _currAssetsVisible = !_currAssetsVisible;

            GUI.Label(new Rect(10, 70, 100, 22), "Functions");

            if (GUI.Button(new Rect(20, 90, 220, 22), "Write current session to Configs."))
            {
                WriteSessionConfigs();
            }

            if (_currentSelectedObject != null)
            {
                DrawPositioningControls();
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 10000));
        }

        private string _xPosition = "";
        private string _yPosition = "";
        private string _zPosition = "";
        private string _rPosition = "";

        private void DrawPositioningControls()
        {
            //if (GUI.Button(new Rect(20, 90, 180, 22), "Pick-up selected object."))
            //{
            //    _objectSelected = !_objectSelected;
            //    //todo pickup object
            //}
            //GUI.Label(new Rect(210, 90, 200, 22), "- Hold CTRL to drop object.");

            bool reorient = false;

            GUI.BeginGroup(new Rect(10, 125, 380, 300));

            GUI.Label(new Rect(0, 0, 200, 22), "Object Placement Controls");

            #region X Position

            GUI.backgroundColor = Color.red;
            GUI.Label(new Rect(10, 25, 100, 22), "X Position");
            if (GUI.RepeatButton(new Rect(100, 25, 30, 22), "<<"))
            {
                _currentSelectedObject.RadPosition.x--;
                reorient = true;
            }
            if (GUI.Button(new Rect(130, 25, 30, 22), "<"))
            {
                _currentSelectedObject.RadPosition.x -= 0.5f;
                reorient = true;
            }
            if (GUI.Button(new Rect(170, 25, 30, 22), ">"))
            {
                _currentSelectedObject.RadPosition.x += 0.5f;
                reorient = true;
            }
            if (GUI.RepeatButton(new Rect(200, 25, 30, 22), ">>"))
            {
                _currentSelectedObject.RadPosition.x++;
                reorient = true;
            }

            _xPosition = GUI.TextField(new Rect(240, 25, 140, 22), _xPosition);
            
            #endregion

            #region Z Position

            GUI.Label(new Rect(10, 75, 100, 22), "Z Position");
            if (GUI.RepeatButton(new Rect(100, 75, 30, 22), "<<"))
            {
                _currentSelectedObject.RadPosition.z--;
                reorient = true;
            }
            if (GUI.Button(new Rect(130, 75, 30, 22), "<"))
            {
                _currentSelectedObject.RadPosition.z -= 0.5f;
                reorient = true;
            }
            if (GUI.Button(new Rect(170, 75, 30, 22), ">"))
            {
                _currentSelectedObject.RadPosition.z += 0.5f;
                reorient = true;
            }
            if (GUI.RepeatButton(new Rect(200, 75, 30, 22), ">>"))
            {
                _currentSelectedObject.RadPosition.z++;
                reorient = true;
            }

            _zPosition = GUI.TextField(new Rect(240, 75, 140, 22), _zPosition);

            #endregion

            #region Y Position

            GUI.backgroundColor = Color.blue;
            GUI.Label(new Rect(10, 50, 100, 22), "Y Position");
            if (GUI.RepeatButton(new Rect(100, 50, 30, 22), "<<"))
            {
                _currentSelectedObject.RadPosition.y--;
                reorient = true;
            }
            if (GUI.Button(new Rect(130, 50, 30, 22), "<"))
            {
                _currentSelectedObject.RadPosition.y -= 0.5f;
                reorient = true;
            }
            if (GUI.Button(new Rect(170, 50, 30, 22), ">"))
            {
                _currentSelectedObject.RadPosition.y += 0.5f;
                reorient = true;
            }
            if (GUI.RepeatButton(new Rect(200, 50, 30, 22), ">>"))
            {
                _currentSelectedObject.RadPosition.y++;
                reorient = true;
            }

            _yPosition = GUI.TextField(new Rect(240, 50, 140, 22), _yPosition);
            
            #endregion

            #region R Offset

            GUI.backgroundColor = Color.green;
            GUI.Label(new Rect(10, 100, 100, 22), "Altitude");
            if (GUI.RepeatButton(new Rect(100, 100, 30, 22), "<<"))
            {
                _currentSelectedObject.RadOffset--;
                reorient = true;
            }
            if (GUI.Button(new Rect(130, 100, 30, 22), "<"))
            {
                _currentSelectedObject.RadOffset -= 0.5f;
                reorient = true;
            }
            if (GUI.Button(new Rect(170, 100, 30, 22), ">"))
            {
                _currentSelectedObject.RadOffset += 0.5f;
                reorient = true;
            }
            if (GUI.RepeatButton(new Rect(200, 100, 30, 22), ">>"))
            {
                _currentSelectedObject.RadOffset++;
                reorient = true;
            }

            _rPosition = GUI.TextField(new Rect(240, 100, 140, 22), _rPosition);
            
            #endregion

            GUI.backgroundColor = Color.yellow;
            if (GUI.Button(new Rect(310, 125, 70, 22), "Update ^"))
            {
                float floatVal = 0;
                if (float.TryParse(_xPosition, out floatVal)) _currentSelectedObject.RadPosition.x = floatVal;
                if (float.TryParse(_zPosition, out floatVal)) _currentSelectedObject.RadPosition.z = floatVal;
                if (float.TryParse(_yPosition, out floatVal)) _currentSelectedObject.RadPosition.y = floatVal;
                if (float.TryParse(_rPosition, out floatVal)) _currentSelectedObject.RadOffset = floatVal;

                reorient = true;
            }

            #region Orientation

            GUI.backgroundColor = Color.white;
            GUI.Label(new Rect(10, 125, 100, 22), "Orientation");
            if (GUI.Button(new Rect(100, 125, 30, 22), "\x2191"))
            {
                _currentSelectedObject.Orientation = Vector3.up;
                reorient = true;
            }
            if (GUI.Button(new Rect(130, 125, 30, 22), "\x2192"))
            {
                _currentSelectedObject.Orientation = Vector3.right;
                reorient = true;
            }
            if (GUI.Button(new Rect(160, 125, 30, 22), "\x2193"))
            {
                _currentSelectedObject.Orientation = Vector3.down;
                reorient = true;
            }
            if (GUI.Button(new Rect(190, 125, 30, 22), "\x2190"))
            {
                _currentSelectedObject.Orientation = Vector3.left;
                reorient = true;
            }
            if (GUI.Button(new Rect(220, 125, 30, 22), "\x25cb"))
            {
                _currentSelectedObject.Orientation = Vector3.forward;
                reorient = true;
            }
            if (GUI.Button(new Rect(250, 125, 30, 22), "\x25cf"))
            {
                _currentSelectedObject.Orientation = Vector3.back;
                reorient = true;
            }

            #endregion

            #region Rotation

            GUI.Label(new Rect(10, 150, 100, 22), "Rotation");
            _currentSelectedObject.RotAngle = GUI.HorizontalSlider(new Rect(100, 155, 180, 20),
                _currentSelectedObject.RotAngle, 0f, 359f);

            if (Math.Abs(_prevRotationAngle - _currentSelectedObject.RotAngle) > 0.001f)
            {
                _prevRotationAngle = _currentSelectedObject.RotAngle;
                reorient = true;
            }

            #endregion

            if (reorient)
            {
                _xPosition = _currentSelectedObject.RadPosition.x.ToString(CultureInfo.InvariantCulture);
                _yPosition = _currentSelectedObject.RadPosition.y.ToString(CultureInfo.InvariantCulture);
                _zPosition = _currentSelectedObject.RadPosition.z.ToString(CultureInfo.InvariantCulture);
                _rPosition = _currentSelectedObject.RadOffset.ToString(CultureInfo.InvariantCulture);

                _currentSelectedObject.Latitude = GetLatitude(_currentCelestialObj, _currentSelectedObject.RadPosition);
                _currentSelectedObject.Longitude = GetLongitude(_currentCelestialObj, _currentSelectedObject.RadPosition);
                _currentSelectedObject.Reorientate();
            }

            GUI.EndGroup();
        }

        private void DrawSelectedWindow(int windowID)
        {
            if (_currentSelectedObject == null)
            {
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                GUI.Label(new Rect(20, 20, 150, 22), "No object selected.");
                return;
            }

            string latitude = _currentSelectedObject.Latitude.ToString("N6");
            string longitude = _currentSelectedObject.Longitude.ToString("N6");
            string radialOffset = _currentSelectedObject.RadOffset.ToString("N2");

            Vector3 vecPosition = _currentSelectedObject.RadPosition;
            Vector3 vecOrientation = _currentSelectedObject.Orientation;

            GUI.Label(new Rect(10, 20, 150, 22), "Latitude / Longitude:");
            GUI.Label(new Rect(150, 20, 300, 22), latitude + " / " + longitude);

            GUI.Label(new Rect(10, 40, 150, 22), "Radial Offset (Alt.):");
            GUI.Label(new Rect(150, 40, 300, 22), radialOffset);

            GUI.Label(new Rect(10, 60, 150, 22), "Local Position (Vec3):");
            GUI.Label(new Rect(150, 60, 300, 22), vecPosition.ToString());

            GUI.Label(new Rect(10, 80, 150, 22), "Orientation:");
            GUI.Label(new Rect(150, 80, 300, 22), vecOrientation.ToString());

            GUI.Label(new Rect(10,100, 150,22), "Body Name:");
            GUI.Label(new Rect(150, 100, 300, 22), _currentSelectedObject.CelestialBodyName);

            GUI.Label(new Rect(10, 120, 150, 22), "Selected Object:");
            GUI.Label(new Rect(150, 120, 300, 22), _currentSelectedObject.NameID);
        }

        private void DrawAvailAssetWindow(int windowID)
        {
            GUI.Box(new Rect(10, 20, 580, 300), "");

            _availAssetScrollPos = GUI.BeginScrollView(new Rect(10, 20, 580, 300), _availAssetScrollPos,
                new Rect(0, 0, 560, _modelList.Count*25 + 5));

            int i = 0;

            // Model = ModelURL
            // Model[x] = ConfigURL
            foreach (var model in _modelList.Keys)
            {
                bool itemMatches = (model == _currentModelUrl);
                GUI.backgroundColor = new Color(0.3f, itemMatches ? 1f : 0.3f, 0.3f);

                if (GUI.Button(new Rect(5, (i*25) + 5, 550, 22),
                    itemMatches
                        ? string.Format("[ {0} ]", model)
                        : model))
                {
                    _currentModelUrl = itemMatches ? "" : model; // Select / Deselect
                    _currentConfigUrl = _currentConfigUrl == _modelList[model] ? "" : _modelList[model];
                }

                i++;
            }

            GUI.EndScrollView();

            GUI.Label(new Rect(20, 330, 300, 22), "Current body: " + _currentBodyName);

            GUI.backgroundColor = new Color(0.0f, _currentModelUrl != "" ? 0.7f : 0.0f, 0.0f);
            if (GUI.Button(new Rect(480, 330, 100, 30), "Create") && _currentModelUrl != "")
            {
                // Set the current celestial object. (Needs to be set before GetDefaultStaticObject).
                _currentBodyName = FlightGlobals.currentMainBody.bodyName;
                _currentCelestialObj = GetCelestialObject(_currentBodyName);

                StaticObject newObject = GetDefaultStaticObject(_currentModelUrl, _currentConfigUrl);
                if (_instancedList.ContainsKey(_currentModelUrl))
                {
                    _instancedList[_currentModelUrl].Add(newObject);
                }
                else
                {
                    _instancedList.Add(_currentModelUrl,
                        new List<StaticObject>
                        {
                            newObject
                        });
                }

                newObject.CelestialBodyName = _currentCelestialObj.CelestialBodyComponent.name;

                InstantiateStatic(_currentCelestialObj.PQSComponent, newObject);

                // Remove previously highlighted object if there is one.
                if (_currentSelectedObject != null) _currentSelectedObject.Manipulate(false);

                _currentObjectID = newObject.ObjectID;
                _currentSelectedObject = newObject;

                // Highlight new selected object.
                if (_currentSelectedObject != null) _currentSelectedObject.Manipulate(true);
            }

            GUI.backgroundColor = new Color(0.5f, 0.5f, 0.5f);
            if (GUI.Button(new Rect(410, 335, 60, 20), "Close"))
                _availAssetsVisible = false;

            GUI.DragWindow(new Rect(0, 0, 10000, 10000));
        }

        private void DrawCurrAssetWindow(int windowID)
        {
            // ScrollView, temporary background.
            GUI.Box(new Rect(10, 20, 580, 300), "");

            _currAssetScrollPos = GUI.BeginScrollView(new Rect(10, 20, 580, 300), _currAssetScrollPos,
                new Rect(0, 0, 560, _instancedList.Values.Sum(list => list.Count)*25 + 5));

            int i = 0;

            foreach (string objectList in _instancedList.Keys)
            {
                foreach (StaticObject sObject in _instancedList[objectList])
                {
                    bool itemMatches = sObject.ObjectID == _currentObjectID;
                    GUI.backgroundColor = new Color(0.3f, itemMatches ? 1f : 0.3f, 0.3f);

                    if (GUI.Button(new Rect(5, (i*25) + 5, 550, 22),
                        string.Format("{0} (ID: {1})", sObject.ModelUrl, sObject.ObjectID)))
                    {
                        if (itemMatches)
                        {
                            // Deselect
                            if (_currentSelectedObject != null)
                                _currentSelectedObject.Manipulate(false);
                            
                            _currentObjectID = "";
                            _currentSelectedObject = null;
                        }
                        else
                        {
                            if (_currentSelectedObject != null) 
                                _currentSelectedObject.Manipulate(false);

                            _currentObjectID = sObject.ObjectID; // Select
                            _currentSelectedObject = sObject;
                            _currentSelectedObject.Manipulate(true);
                        }
                    }
                    i++;
                }
            }
            GUI.EndScrollView();

            GUI.backgroundColor = new Color(0.5f, 0.5f, 0.5f);
            if (GUI.Button(new Rect(120, 335, 60, 20), "Close"))
                _currAssetsVisible = false;

            GUI.backgroundColor = new Color(_currentSelectedObject != null ? 1 : 0, 0, 0);
            if (GUI.Button(new Rect(10, 330, 100, 30), "Delete"))
            {
                if (_currentSelectedObject != null)
                {
                    Destroy(_currentSelectedObject.StaticGameObject);
                    RemoveCurrentStaticObject(_currentSelectedObject.ModelUrl);
                }

                _currentObjectID = "";
                _currentSelectedObject = null;
            }


            GUI.DragWindow(new Rect(0, 0, 10000, 10000));
        }

        #endregion

        public class CelestialObject
        {
            public CelestialBody CelestialBodyComponent;
            public GameObject CelestialGameObject;
            public PQS PQSComponent;

            public CelestialObject(GameObject parentObject)
            {
                CelestialGameObject = parentObject;
                CelestialBodyComponent = parentObject.GetComponentInChildren<CelestialBody>();
                PQSComponent = parentObject.GetComponentInChildren<PQS>();

                if (CelestialBodyComponent == null)
                    Extensions.LogError("Could not obtain CelestialBody component from: " + parentObject.name);

                if (PQSComponent == null)
                    Extensions.LogError("Could not obtain PQS component from: " + parentObject.name);
            }
        }

        private class StaticObject
        {
            public readonly string ConfigURL;
            public readonly string ModelUrl;
            public readonly string NameID;
            public readonly string ObjectID; 
            public readonly float VisRange;

            public string CelestialBodyName = "";

            public double Latitude;
            public double Longitude;
            public Vector3 Orientation;
            public PQSCity PQSCityComponent;

            public float RadOffset;
            public Vector3 RadPosition;

            public float RotAngle;
            public GameObject StaticGameObject;

            private List<Renderer> _rendererComponents;
            private List<Collider> _colliderComponents; 

            public void Manipulate(bool inactive)
            {
                Manipulate(inactive, XKCDColors.BlueyGrey);
            }
            public void Manipulate(bool inactive, Color highlightColor)
            {
                if (StaticGameObject == null)
                {
                    Extensions.LogWarning(NameID + " has no GameObject attached.");
                    return;
                }

                #region Colliders
                if (_colliderComponents == null || _colliderComponents.Count == 0)
                {
                    var colliderList = StaticGameObject.GetComponentsInChildren<Collider>();

                    if (colliderList.Length > 0)
                    {
                        _colliderComponents = new List<Collider>(colliderList);
                    }
                    else Extensions.LogWarning(NameID + " has no collider components.");
                }

                if (_colliderComponents != null && _colliderComponents.Count > 0)
                {
                    foreach (var collider in _colliderComponents)
                    {
                        collider.enabled = !inactive;
                    }
                }
                #endregion

                #region Highlight
                if ((_rendererComponents == null || _rendererComponents.Count == 0))
                {
                    var rendererList = StaticGameObject.GetComponentsInChildren<Renderer>();
                    if (rendererList.Length == 0)
                    {
                        Extensions.LogWarning(NameID + " has no renderer components.");
                        return;
                    }
                    _rendererComponents = new List<Renderer>(rendererList);
                }

                if (!inactive)
                    highlightColor = new Color(0, 0, 0, 0);

                foreach (var renderer in _rendererComponents)
                {
                    renderer.material.SetFloat("_RimFalloff", 1.8f);
                    renderer.material.SetColor("_RimColor", highlightColor);
                }
                #endregion
            }

            public StaticObject(Vector3 radialPosition, float rotationAngle, float radiusOffset,
                Vector3 objectOrientation, float visibilityRange, string modelUrl, string configUrl,
                string celestialBodyName, string objectID = "")
            {
                RadPosition = radialPosition;
                RotAngle = rotationAngle;
                RadOffset = radiusOffset;
                Orientation = objectOrientation;
                VisRange = visibilityRange;

                CelestialBodyName = celestialBodyName;

                ModelUrl = modelUrl;
                ConfigURL = configUrl;

                ObjectID = objectID;

                if (string.IsNullOrEmpty(ObjectID))
                    ObjectID =
                        (visibilityRange + rotationAngle + radiusOffset + objectOrientation.magnitude +
                         radialPosition.magnitude + Random.Range(-1000000f, 1000000f))
                            .ToString("N2");

                NameID = string.Format("{0} ({1})", modelUrl.Substring(modelUrl.LastIndexOf('/') + 1), ObjectID);
            }

            public void Reorientate()
            {
                if (PQSCityComponent == null) return;
                PQSCityComponent.repositionRadial = RadPosition;
                PQSCityComponent.repositionRadiusOffset = RadOffset;
                PQSCityComponent.reorientFinalAngle = RotAngle;
                PQSCityComponent.reorientInitialUp = Orientation;
                PQSCityComponent.Orientate();
            }

            public override string ToString()
            {
                return
                    string.Format(
                        "NameID: {0}, ObjectID: {1}, CelestialBodyName: {2}, ModelUrl: {3}, ConfigUrl: {4}, RPos: {5}",
                        NameID, ObjectID, CelestialBodyName, ModelUrl, ConfigURL, RadPosition);
            }
        }
    }

    public static class Extensions
    {
        public static void LogError(string message)
        {
            Debug.LogError("KerbTown: " + message);
        }

        public static void LogWarning(string message)
        {
            Debug.LogWarning("KerbTown: " + message);
        }

        public static void LogInfo(string message)
        {
            Debug.Log("KerbTown: " + message);
        }

        public static int SecondLastIndex(this string str, char searchCharacter)
        {
            int lastIndex = str.LastIndexOf(searchCharacter);

            if (lastIndex != -1)

            {
                return str.LastIndexOf(searchCharacter, lastIndex - 1);
            }

            return -1;
        }

        public static int SecondLastIndex(this string str, string searchString)
        {
            int lastIndex = str.LastIndexOf(searchString, StringComparison.Ordinal);

            if (lastIndex != -1)
            {
                return str.LastIndexOf(searchString, lastIndex - 1, StringComparison.Ordinal);
            }

            return -1;
        }
    }
}