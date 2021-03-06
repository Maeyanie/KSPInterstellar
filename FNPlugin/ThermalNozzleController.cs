﻿extern alias ORSv1_4_3;
using ORSv1_4_3::OpenResourceSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using TweakScale;

namespace FNPlugin
{
    class ThermalNozzleController : FNResourceSuppliableModule, IUpgradeableModule , IRescalable<ThermalNozzleController>
    {
		// Persistent True
		[KSPField(isPersistant = true)]
		public bool IsEnabled;
		[KSPField(isPersistant = true)]
		public bool isHybrid = false;
		[KSPField(isPersistant = true)]
		public bool isupgraded = false;
		[KSPField(isPersistant = true)]
		public bool engineInit = false;
		[KSPField(isPersistant = true)]
		public int fuel_mode = 0;

		//Persistent False
		[KSPField(isPersistant = false)]
		public bool isJet = false;
		[KSPField(isPersistant = false)]
		public float upgradeCost;
		[KSPField(isPersistant = false)]
		public string originalName;
		[KSPField(isPersistant = false)]
		public string upgradedName;
		[KSPField(isPersistant = false)]
		public string upgradeTechReq = null;
        [KSPField(isPersistant = false)]
        public float powerTrustMultiplier = 1.0f;
        [KSPField(isPersistant = false)]
        public float IspTempMultOffset = 0f;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Radius", guiUnits = "m")]
        public float radius;
        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Exit Area", guiUnits = " m2")]
        public float exitArea = 1;
        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Mass", guiUnits = " t")]
        public float partMass = 1;

		//External
		public bool static_updating = true;
		public bool static_updating2 = true;

		//GUI
		[KSPField(isPersistant = false, guiActive = true, guiName = "Type")]
		public string engineType = ":";
		[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Fuel Mode")]
		public string fuelmode;
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Fuel Isp Multiplier")]
        public float ispPropellantMultiplier = 1;
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Fuel Thrust Multiplier")]
        public float thrustPropellantMultiplier = 1;
		[KSPField(isPersistant = false, guiActive = true, guiName = "Upgrade Cost")]
		public string upgradeCostStr;
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Static Presure")]
        public string staticPresure;
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Treshold", guiUnits = " kN")]
        public float pressureTreshold;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Radius Modifier")]
        public string radiusModifier;
        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Vacuum")]
        public string vacuumPerformance;
        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Sea")]
        public string surfacePerformance;

		//Internal
		protected float maxISP;
		protected float minISP;
		protected float assThermalPower;
		protected float powerRatio = 0.358f;
		protected float engineMaxThrust;
		protected bool isLFO = false;
		
        
		protected ConfigNode[] propellants;
		protected VInfoBox fuel_gauge;
		protected bool hasrequiredupgrade = false;
		protected bool hasstarted = false;
		protected ModuleEngines myAttachedEngine;
		protected IThermalSource myAttachedReactor;
		protected bool currentpropellant_is_jet = false;
		protected double fuel_flow_rate = 0;
		protected int thrustLimitRatio = 0;
		protected double old_thermal_power = 0;
		protected double old_isp = 0;
		protected double current_isp = 0;
		protected double old_intake = 0;
        //protected bool flameFxOn = true;
        protected float atmospheric_limit;
        protected float old_atmospheric_limit;
        protected float heatExchangerThrustDivisor;
        protected float maxPressureTresholdAtKerbinSurface;

		//Static
		static Dictionary<string, double> intake_amounts = new Dictionary<string, double>();
		static Dictionary<string, double> fuel_flow_amounts = new Dictionary<string, double>();

        public String UpgradeTechnology { get { return upgradeTechReq; } }

		[KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Toggle Propellant", active = true)]
		public void TogglePropellant() 
        {
			fuel_mode++;
			if (fuel_mode >= propellants.Length) 
				fuel_mode = 0;
			
			setupPropellants();
		}

        public void OnRescale(TweakScale.ScalingFactor factor)
        {
            // update variables
            radius *= factor.relative.linear;
            exitArea *= factor.relative.quadratic;

            // update simulation
            UpdateRadiusModifier();
        }
        
		[KSPAction("Toggle Propellant")]
		public void TogglePropellantAction(KSPActionParam param) 
        {
			TogglePropellant();
		}

		[KSPEvent(guiActive = true, guiName = "Retrofit", active = true)]
		public void RetrofitEngine() 
        {
			if (ResearchAndDevelopment.Instance == null) { return;}
			if (isupgraded || ResearchAndDevelopment.Instance.Science < upgradeCost) { return; }
			upgradePartModule ();
            ResearchAndDevelopment.Instance.AddScience(-upgradeCost, TransactionReasons.RnDPartPurchase);
		}

		public void upgradePartModule() 
        {
			engineType = upgradedName;
			propellants = FNNozzleController.getPropellantsHybrid();
			isHybrid = true;
			isupgraded = true;
		}

		public ConfigNode[] getPropellants() 
        {
			return propellants;
		}

        public void OnEditorAttach() 
        {
            FindAttachedThermalSource();
            estimateEditorPerformance();
        }

		public override void OnStart(PartModule.StartState state) 
        {
            engineType = originalName;
            myAttachedEngine = this.part.Modules["ModuleEngines"] as ModuleEngines;
            // find attached thermal source
            FindAttachedThermalSource();

            if (state == StartState.Editor) 
            {
                part.OnEditorAttach += OnEditorAttach;
                propellants = getPropellants(isJet);
                if (this.HasTechsRequiredToUpgrade() && isJet)
                {
                    isupgraded = true;
                    upgradePartModule();
                }
                setupPropellants();
                estimateEditorPerformance();
                return;
            }
            else
                UpdateRadiusModifier();

			fuel_gauge = part.stackIcon.DisplayInfo();
			
            // if engine isn't already initialised, initialise it
			if (engineInit == false) 
				engineInit = true;
			
			// if we can upgrade, let's do so
			if (isupgraded && isJet) 
				upgradePartModule ();
            else 
            {
                if (this.HasTechsRequiredToUpgrade() && isJet)
                    hasrequiredupgrade = true;

				// if not, use basic propellants
				propellants = getPropellants (isJet);
			}

            heatExchangerThrustDivisor = (float)GetHeatExchangerThrustDivisor();
            maxPressureTresholdAtKerbinSurface = exitArea * (float)GameConstants.EarthAthmospherePresureAtSeaLevel;
			setupPropellants();
			hasstarted = true;
		}

        private void FindAttachedThermalSource()
        {
            foreach (AttachNode attach_node in part.attachNodes)
            {
                if (attach_node.attachedPart == null) continue;

                List<IThermalSource> sources = attach_node.attachedPart.FindModulesImplementing<IThermalSource>();

                if (sources.Count <= 0) continue;

                myAttachedReactor = sources.First();

                if (myAttachedReactor != null) break;
            }
        }

		public override void OnUpdate() 
        {
            // Note: does not seem to be called while in edit mode
            staticPresure = (GameConstants.EarthAthmospherePresureAtSeaLevel * FlightGlobals.getStaticPressure(vessel.transform.position)).ToString("0.0000") + " kPa";
            pressureTreshold = exitArea * (float)GameConstants.EarthAthmospherePresureAtSeaLevel * (float)FlightGlobals.getStaticPressure(vessel.transform.position);

			Fields["engineType"].guiActive = isJet;
			if (ResearchAndDevelopment.Instance != null && isJet) 
            {
				Events ["RetrofitEngine"].active = !isupgraded && ResearchAndDevelopment.Instance.Science >= upgradeCost && hasrequiredupgrade;
				upgradeCostStr = ResearchAndDevelopment.Instance.Science.ToString("0") + " / " + upgradeCost;
			} 
            else
				Events ["RetrofitEngine"].active = false;
			
			Fields["upgradeCostStr"].guiActive = !isupgraded  && hasrequiredupgrade && isJet;

			if (myAttachedEngine != null) 
            {
				if (myAttachedEngine.isOperational && !IsEnabled) 
                {
					IsEnabled = true;
					part.force_activate ();
				}
				updatePropellantBar ();
			}
		}
        
		public void updatePropellantBar() 
        {
			float currentpropellant = 0;
			float maxpropellant = 0;

            List<PartResource> partresources = part.GetConnectedResources(myAttachedEngine.propellants.FirstOrDefault().name).ToList();
			
			foreach (PartResource partresource in partresources) 
            {
				currentpropellant += (float) partresource.amount;
				maxpropellant += (float)partresource.maxAmount;
			}

			if (fuel_gauge != null  && fuel_gauge.infoBoxRef != null) 
            {
				if (myAttachedEngine.isOperational) 
                {
					if (!fuel_gauge.infoBoxRef.expanded) 
						fuel_gauge.infoBoxRef.Expand ();
					
					fuel_gauge.length = 2;
					if (maxpropellant > 0) 
						fuel_gauge.SetValue (currentpropellant / maxpropellant);
					else
						fuel_gauge.SetValue (0);
				} 
                else if (!fuel_gauge.infoBoxRef.collapsed) 
				    fuel_gauge.infoBoxRef.Collapse ();
			}
		}

		public void setupPropellants() 
        {
            ConfigNode chosenpropellant = propellants[fuel_mode];
			ConfigNode[] assprops = chosenpropellant.GetNodes("PROPELLANT");
			List<Propellant> list_of_propellants = new List<Propellant>();
			// loop though propellants until we get to the selected one, then set it up
			foreach(ConfigNode prop_node in assprops) 
            {
				fuelmode = chosenpropellant.GetValue("guiName");
				ispPropellantMultiplier = float.Parse(chosenpropellant.GetValue("ispMultiplier"));

                // get optional thrustMultiplier
                if (chosenpropellant.HasValue("thrustMultiplier"))
                    thrustPropellantMultiplier = float.Parse(chosenpropellant.GetValue("thrustMultiplier"));
                else
                    thrustPropellantMultiplier = 1;

				isLFO = bool.Parse(chosenpropellant.GetValue("isLFO"));
                currentpropellant_is_jet = chosenpropellant.HasValue("isJet") ? bool.Parse(chosenpropellant.GetValue("isJet")) : false;

				Propellant curprop = new Propellant();
				curprop.Load(prop_node);
				if (curprop.drawStackGauge && HighLogic.LoadedSceneIsFlight) 
                {
					curprop.drawStackGauge = false;

					if (currentpropellant_is_jet) 
						fuel_gauge.SetMessage("Atmosphere");
					else 
                    {
						fuel_gauge.SetMessage(curprop.name);
                        myAttachedEngine.thrustPercentage = 100;
                        part.temperature = 1;
					}

					fuel_gauge.SetMsgBgColor(XKCDColors.DarkLime);
					fuel_gauge.SetMsgTextColor(XKCDColors.ElectricLime);
					fuel_gauge.SetProgressBarColor(XKCDColors.Yellow);
					fuel_gauge.SetProgressBarBgColor(XKCDColors.DarkLime);
					fuel_gauge.SetValue(0f);
				}
				list_of_propellants.Add(curprop);
			}
            
			// update the engine with the new propellants
			if (PartResourceLibrary.Instance.GetDefinition(list_of_propellants[0].name) != null) 
            {
				myAttachedEngine.propellants.Clear();
				myAttachedEngine.propellants = list_of_propellants;
				myAttachedEngine.SetupPropellant();
			}

            if (HighLogic.LoadedSceneIsFlight) 
            { // you can have any fuel you want in the editor but not in flight
                // should we switch to another propellant because we have none of this one?
                bool next_propellant = false;
                foreach (Propellant curEngine_propellant in myAttachedEngine.propellants) 
                {
                    var partresources = part.GetConnectedResources(curEngine_propellant.name);

                    if (!partresources.Any()|| !PartResourceLibrary.Instance.resourceDefinitions.Contains(list_of_propellants[0].name)) 
                        next_propellant = true;
                }

                // do the switch if needed
                if (next_propellant && fuel_mode != 1) // always shows the prefered default fuel
                    TogglePropellant();
            } 
            else 
            {
                if (!PartResourceLibrary.Instance.resourceDefinitions.Contains(list_of_propellants[0].name) && fuel_mode != 1) // Still ignore propellants that don't exist
                    TogglePropellant();
                
                estimateEditorPerformance(); // update editor estimates
            }

		}

        public void updateIspEngineParams(double athmosphere_isp_efficiency = 1, double max_thrust_in_space = 0) 
        {
			// recaculate ISP based on power and core temp available
			FloatCurve newISP = new FloatCurve();
			FloatCurve vCurve = new FloatCurve ();

            maxISP = (float)(Math.Sqrt((double)myAttachedReactor.CoreTemperature) * (PluginHelper.IspCoreTempMult + IspTempMultOffset) * GetIspPropellantModifier());
            
			if (!currentpropellant_is_jet) 
            {
                if (maxPressureTresholdAtKerbinSurface <= max_thrust_in_space && FlightGlobals.getStaticPressure(vessel.transform.position) <= 1)
                {
                    var min_engine_thrust = Math.Max(max_thrust_in_space - maxPressureTresholdAtKerbinSurface, 0.00001);
                    var minThrustAtmosphereRatio = min_engine_thrust / Math.Max(max_thrust_in_space, 0.000001);
                    minISP = maxISP * (float)minThrustAtmosphereRatio * heatExchangerThrustDivisor;
                    newISP.Add(0, Mathf.Min(maxISP, PluginHelper.MaxThermalNozzleIsp), 0, 0);
                    newISP.Add(1, Mathf.Min(minISP, PluginHelper.MaxThermalNozzleIsp), 0, 0);
                }
                else
                    newISP.Add(0, Mathf.Min(maxISP * (float)athmosphere_isp_efficiency, PluginHelper.MaxThermalNozzleIsp), 0, 0);

				myAttachedEngine.useVelocityCurve = false;
				myAttachedEngine.useEngineResponseTime = false;
			} 
            else 
            {
				if (myAttachedReactor.shouldScaleDownJetISP ()) 
                {
					maxISP = maxISP*2.0f/3.0f;
					if (maxISP > 300) 
						maxISP = maxISP / 2.5f;
				}
                newISP.Add(0, Mathf.Min(maxISP * 4.0f / 5.0f, PluginHelper.MaxThermalNozzleIsp));
                newISP.Add(0.15f, Mathf.Min(maxISP, PluginHelper.MaxThermalNozzleIsp));
                newISP.Add(0.3f, Mathf.Min(maxISP * 4.0f / 5.0f, PluginHelper.MaxThermalNozzleIsp));
                newISP.Add(1, Mathf.Min(maxISP * 2.0f / 3.0f, PluginHelper.MaxThermalNozzleIsp));
				vCurve.Add(0, 1.0f);
                vCurve.Add((float)(maxISP * PluginHelper.GravityConstant * 1.0 / 3.0), 1.0f);
                vCurve.Add((float)(maxISP * PluginHelper.GravityConstant), 1.0f);
                vCurve.Add((float)(maxISP * PluginHelper.GravityConstant * 4.0 / 3.0), 0);
				myAttachedEngine.useVelocityCurve = true;
				myAttachedEngine.useEngineResponseTime = true;
				myAttachedEngine.ignitionThreshold = 0.01f;
			}

			myAttachedEngine.atmosphereCurve = newISP;
			myAttachedEngine.velocityCurve = vCurve;
			assThermalPower = myAttachedReactor.MaximumPower;
            if (myAttachedReactor is InterstellarFusionReactor) 
                assThermalPower = assThermalPower * 0.95f;
		}

		public float GetAtmosphericLimit() 
        {
			float atmospheric_limit = 1.0f;
            if (currentpropellant_is_jet) 
            {
                string resourcename = myAttachedEngine.propellants[0].name;
                double currentintakeatm = getIntakeAvailable(vessel, resourcename);
                if (getFuelRateThermalJetsForVessel(vessel, resourcename) > 0) 
                {
                    // divide current available intake resource by fuel useage across all engines
                    atmospheric_limit = (float)Math.Min(currentintakeatm / (getFuelRateThermalJetsForVessel(vessel, resourcename)), 1.0);
                }
                old_intake = currentintakeatm;
            }
            atmospheric_limit = Mathf.MoveTowards(old_atmospheric_limit, atmospheric_limit, 0.1f);
            old_atmospheric_limit = atmospheric_limit;
			return atmospheric_limit;
		}

		public double getNozzleFlowRate() 
        {
			return fuel_flow_rate;
		}

		public bool getUpdating() 
        {
			return static_updating;
		}

		public bool hasStarted() 
        {
			return hasstarted;
		}

        public void estimateEditorPerformance() 
        {
            //bool attached_reactor_upgraded = false;
            FloatCurve atmospherecurve = new FloatCurve();
            float thrust = 0;
            UpdateRadiusModifier();

            if (myAttachedReactor != null) 
            {
                //if (myAttachedReactor is IUpgradeableModule) {
                //    IUpgradeableModule upmod = myAttachedReactor as IUpgradeableModule;
                //    if (upmod.HasTechsRequiredToUpgrade()) {
                //        attached_reactor_upgraded = true;
                //    }
                //}

                maxISP = (float)(Math.Sqrt((double)myAttachedReactor.CoreTemperature) * (PluginHelper.IspCoreTempMult + IspTempMultOffset) * GetIspPropellantModifier());
                minISP = maxISP * 0.4f;
                atmospherecurve.Add(0, maxISP, 0, 0);
                atmospherecurve.Add(1, minISP, 0, 0);

                thrust = (float)(myAttachedReactor.MaximumPower * GetPowerTrustModifier() * GetHeatTrustModifier() / PluginHelper.GravityConstant / maxISP);
                myAttachedEngine.maxThrust = thrust;
                myAttachedEngine.atmosphereCurve = atmospherecurve;
            } 
            else 
            {
                atmospherecurve.Add(0, 0.00001f, 0, 0);
                myAttachedEngine.maxThrust = thrust;
                myAttachedEngine.atmosphereCurve = atmospherecurve;
            }
        }

        private double GetIspPropellantModifier()
        {
            double ispModifier = (PluginHelper.IspNtrPropellantModifierBase == 0 ? ispPropellantMultiplier
                : (PluginHelper.IspNtrPropellantModifierBase + ispPropellantMultiplier) / (1.0f + PluginHelper.IspNtrPropellantModifierBase));
            return ispModifier;
        }


		public override void OnFixedUpdate() 
        {
            staticPresure = (GameConstants.EarthAthmospherePresureAtSeaLevel * FlightGlobals.getStaticPressure(vessel.transform.position)).ToString("0.0000") + " kPa";
            
            // note: does not seem to be called in edit mode

            if (myAttachedEngine.isOperational && myAttachedEngine.currentThrottle > 0 && myAttachedReactor != null)
                GenerateTrustFromReactorHeat();
            else
            {
                if (myAttachedEngine.realIsp > 0)
                {
                    atmospheric_limit = GetAtmosphericLimit();
                    double vcurve_at_current_velocity = 1;
                    if (myAttachedEngine.useVelocityCurve)
                        vcurve_at_current_velocity = myAttachedEngine.velocityCurve.Evaluate((float)vessel.srf_velocity.magnitude);

                    fuel_flow_rate = myAttachedEngine.maxThrust / myAttachedEngine.realIsp / PluginHelper.GravityConstant / 0.005 * TimeWarp.fixedDeltaTime;
                    if (vcurve_at_current_velocity > 0 && !double.IsInfinity(vcurve_at_current_velocity) && !double.IsNaN(vcurve_at_current_velocity))
                        fuel_flow_rate = fuel_flow_rate / vcurve_at_current_velocity;
                }
                else
                    fuel_flow_rate = 0;

                if (currentpropellant_is_jet)
                    part.temperature = 1;

                if (myAttachedReactor == null && myAttachedEngine.isOperational && myAttachedEngine.currentThrottle > 0)
                {
                    myAttachedEngine.Events["Shutdown"].Invoke();
                    ScreenMessages.PostScreenMessage("Engine Shutdown: No reactor attached!", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            //tell static helper methods we are currently updating things
			static_updating = true;
			static_updating2 = true;
		}

        private void GenerateTrustFromReactorHeat()
        {
            if (!myAttachedReactor.IsActive)
                myAttachedReactor.enableIfPossible();

            // determine ISP
            if (currentpropellant_is_jet)
            {
                updateIspEngineParams();
                this.current_isp = myAttachedEngine.atmosphereCurve.Evaluate((float)Math.Min(FlightGlobals.getStaticPressure(vessel.transform.position), 1.0));
            }
            else
            {
                maxISP = (float)(Math.Sqrt((double)myAttachedReactor.CoreTemperature) * (PluginHelper.IspCoreTempMult + IspTempMultOffset) * GetIspPropellantModifier());
                assThermalPower = myAttachedReactor is InterstellarFusionReactor ? myAttachedReactor.MaximumPower * 0.95f : myAttachedReactor.MaximumPower;
            }

            // get the flameout safety limit
            if (currentpropellant_is_jet)
            {
                int pre_coolers_active = vessel.FindPartModulesImplementing<FNModulePreecooler>().Where(prc => prc.isFunctional()).Count();
                int intakes_open = vessel.FindPartModulesImplementing<ModuleResourceIntake>().Where(mre => mre.intakeEnabled).Count();

                double proportion = Math.Pow((double)(intakes_open - pre_coolers_active) / (double)intakes_open, 0.1);
                if (double.IsNaN(proportion) || double.IsInfinity(proportion))
                    proportion = 1;

                float temp = (float)Math.Max((Math.Sqrt(vessel.srf_velocity.magnitude) * 20.0 / GameConstants.atmospheric_non_precooled_limit) * part.maxTemp * proportion, 1);
                if (temp > part.maxTemp - 10.0f)
                {
                    ScreenMessages.PostScreenMessage("Engine Shutdown: Catastrophic overheating was imminent!", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    myAttachedEngine.Shutdown();
                    part.temperature = 1;
                }
                else
                    part.temperature = temp;
            }

            double thermal_consume_total = assThermalPower * TimeWarp.fixedDeltaTime * myAttachedEngine.currentThrottle * GetAtmosphericLimit();
            double thermal_power_received = consumeFNResource(thermal_consume_total, FNResourceManager.FNRESOURCE_THERMALPOWER) / TimeWarp.fixedDeltaTime;

            if (thermal_power_received * TimeWarp.fixedDeltaTime < thermal_consume_total)
                thermal_power_received += consumeFNResource(thermal_consume_total - thermal_power_received * TimeWarp.fixedDeltaTime, FNResourceManager.FNRESOURCE_CHARGED_PARTICLES) / TimeWarp.fixedDeltaTime;

            consumeFNResource(thermal_power_received * TimeWarp.fixedDeltaTime, FNResourceManager.FNRESOURCE_WASTEHEAT);
            
            // calculate max thrust
            double engineMaxThrust = 0.01;
            if (assThermalPower > 0)
            {
                double ispRatio = currentpropellant_is_jet ? this.current_isp / maxISP : 1;
                double thrust_limit = myAttachedEngine.thrustPercentage / 100.0;
                engineMaxThrust = Math.Max(thrust_limit * GetPowerTrustModifier() * GetHeatTrustModifier() * thermal_power_received / maxISP / PluginHelper.GravityConstant * heatExchangerThrustDivisor * ispRatio / myAttachedEngine.currentThrottle, 0.01);
            }

            double max_thrust_in_space = engineMaxThrust / myAttachedEngine.thrustPercentage * 100.0;
            double engine_thrust = max_thrust_in_space;
            
            // update engine thrust/ISP for thermal noozle
            if (!currentpropellant_is_jet)
            {
                pressureTreshold = exitArea * (float)GameConstants.EarthAthmospherePresureAtSeaLevel * (float)FlightGlobals.getStaticPressure(vessel.transform.position);
                engine_thrust = Math.Max(max_thrust_in_space - pressureTreshold, 0.00001);
                var thrustAtmosphereRatio = engine_thrust / Math.Max(max_thrust_in_space, 0.000001);
                var isp_reduction_fraction = thrustAtmosphereRatio * heatExchangerThrustDivisor;
                updateIspEngineParams(isp_reduction_fraction, max_thrust_in_space);
                this.current_isp = maxISP * isp_reduction_fraction;
            }

            if (!double.IsInfinity(engine_thrust) && !double.IsNaN(engine_thrust))
                myAttachedEngine.maxThrust = thrustPropellantMultiplier == 1f ? (float)engine_thrust : (float)(engine_thrust * thrustPropellantMultiplier);
            else
                myAttachedEngine.maxThrust = 0.000001f;

            // amount of fuel being used at max throttle with no atmospheric limits
            if (current_isp > 0)
            {
                double vcurve_at_current_velocity = 1;

                if (myAttachedEngine.useVelocityCurve && myAttachedEngine.velocityCurve != null)
                    vcurve_at_current_velocity = myAttachedEngine.velocityCurve.Evaluate((float)vessel.srf_velocity.magnitude);

                fuel_flow_rate = engine_thrust / current_isp / PluginHelper.GravityConstant / 0.005 * TimeWarp.fixedDeltaTime;
                if (vcurve_at_current_velocity > 0 && !double.IsInfinity(vcurve_at_current_velocity) && !double.IsNaN(vcurve_at_current_velocity))
                    this.fuel_flow_rate = fuel_flow_rate / vcurve_at_current_velocity;

                if (atmospheric_limit > 0 && !double.IsInfinity(atmospheric_limit) && !double.IsNaN(atmospheric_limit))
                    this.fuel_flow_rate = fuel_flow_rate / atmospheric_limit;
            }
        }

		public override string GetInfo() 
        {
			bool upgraded = false;
            if (this.HasTechsRequiredToUpgrade())
                upgraded = true;

            ConfigNode[] prop_nodes = upgraded && isJet ? getPropellantsHybrid() : getPropellants(isJet);
			
			string return_str = "Thrust: Variable\n";
			foreach (ConfigNode propellant_node in prop_nodes) 
            {
				float ispMultiplier = float.Parse(propellant_node.GetValue("ispMultiplier"));
				string guiname = propellant_node.GetValue("guiName");
                return_str = return_str + "--" + guiname + "--\n" + "ISP: " + ispMultiplier.ToString("0.000") + " x " + (PluginHelper.IspCoreTempMult + IspTempMultOffset).ToString("0.000") + " x Sqrt(Core Temperature)" + "\n";
			}
			return return_str;
		}

        public override int getPowerPriority() 
        {
            return 1;
        }

		// Static Methods
		// Amount of intake air available to use of a particular resource type
		public static double getIntakeAvailable(Vessel vess, string resourcename) 
        {
            List<FNNozzleController> nozzles = vess.FindPartModulesImplementing<FNNozzleController>();
            bool updating = true;
            foreach (FNNozzleController nozzle in nozzles)
            {
                if (!nozzle.static_updating)
                {
                    updating = false;
                    break;
                }
            }

            if (updating)
            {
                nozzles.ForEach(nozzle => nozzle.static_updating = false);
                List<PartResource> partresources = vess.rootPart.GetConnectedResources(resourcename).ToList();
                double currentintakeatm = 0;
                partresources.ForEach(partresource => currentintakeatm += partresource.amount);

                if (intake_amounts.ContainsKey(resourcename))
                    intake_amounts[resourcename] = currentintakeatm;
                else
                    intake_amounts.Add(resourcename, currentintakeatm);
            }

            if (intake_amounts.ContainsKey(resourcename))
                return Math.Max(intake_amounts[resourcename], 0);
            
			return 0.00001;
		}

		// enumeration of the fuel useage rates of all jets on a vessel
		public static int getEnginesRunningOfTypeForVessel (Vessel vess, string resourcename) 
        {
			List<FNNozzleController> nozzles = vess.FindPartModulesImplementing<FNNozzleController> ();
			int engines = 0;
			foreach (FNNozzleController nozzle in nozzles) 
            {
				ConfigNode[] prop_node = nozzle.getPropellants ();

                if (prop_node == null || prop_node[nozzle.fuel_mode] == null) continue;

                ConfigNode[] assprops = prop_node[nozzle.fuel_mode].GetNodes("PROPELLANT");
                if (assprops[0].GetValue("name").Equals(resourcename) && nozzle.getNozzleFlowRate() > 0)
                {
                    engines++;
                }
			}
			return engines;
		}

		// enumeration of the fuel useage rates of all jets on a vessel
		public static double getFuelRateThermalJetsForVessel (Vessel vess, string resourcename) 
        {
			List<FNNozzleController> nozzles = vess.FindPartModulesImplementing<FNNozzleController> ();
			int engines = 0;
			bool updating = true;
			foreach (FNNozzleController nozzle in nozzles) 
            {
				ConfigNode[] prop_node = nozzle.getPropellants ();

                if (prop_node == null) continue; 

				ConfigNode[] assprops = prop_node [nozzle.fuel_mode].GetNodes ("PROPELLANT");

                if (prop_node[nozzle.fuel_mode] == null || !assprops[0].GetValue("name").Equals(resourcename)) continue;

				if (!nozzle.static_updating2) 
					updating = false;
							
				if (nozzle.getNozzleFlowRate () > 0) 
					engines++;
			}

			if (updating) 
            {
				double enum_rate = 0;
				foreach (FNNozzleController nozzle in nozzles) 
                {
					ConfigNode[] prop_node = nozzle.getPropellants ();

                    if (prop_node == null) continue;

                    ConfigNode[] assprops = prop_node [nozzle.fuel_mode].GetNodes ("PROPELLANT");

                    if (prop_node[nozzle.fuel_mode] == null || !assprops[0].GetValue("name").Equals(resourcename)) continue;

					enum_rate += nozzle.getNozzleFlowRate ();
					nozzle.static_updating2 = false;
				}

				if (fuel_flow_amounts.ContainsKey (resourcename)) 
					fuel_flow_amounts [resourcename] = enum_rate;
				else
				    fuel_flow_amounts.Add (resourcename, enum_rate);
			}

			if (fuel_flow_amounts.ContainsKey (resourcename)) 
				return fuel_flow_amounts [resourcename];

			return 0.1;
		}


        public static ConfigNode[] getPropellants(bool isJet) 
        {
            ConfigNode[] propellantlist = isJet
                ? GameDatabase.Instance.GetConfigNodes("ATMOSPHERIC_NTR_PROPELLANT")
                : GameDatabase.Instance.GetConfigNodes("BASIC_NTR_PROPELLANT");

            if (propellantlist == null) 
                PluginHelper.showInstallationErrorMessage();

            return propellantlist;
        }

        private double GetHeatTrustModifier()
        {
            double coretempthreshold = PluginHelper.TrustCoreTempThreshold;
            double lowcoretempbase = PluginHelper.LowCoreTempBaseTrust;

            return coretempthreshold <= 0 
                ? 1.0 
                : myAttachedReactor.CoreTemperature < coretempthreshold
                    ? (myAttachedReactor.CoreTemperature + lowcoretempbase) / (coretempthreshold + lowcoretempbase)
                    : 1.0 + PluginHelper.HighCoreTempTrustMult * Math.Max(Math.Log10(myAttachedReactor.CoreTemperature / coretempthreshold), 0);
        }

        private double GetPowerTrustModifier()
        {
            return GameConstants.BaseTrustPowerMultiplier * PluginHelper.GlobalThermalNozzlePowerMaxTrustMult * powerTrustMultiplier;
        }

        private void UpdateRadiusModifier()
        {
            if (myAttachedReactor != null)
            {
                Fields["vacuumPerformance"].guiActiveEditor = true;
                Fields["radiusModifier"].guiActiveEditor = true;
                Fields["surfacePerformance"].guiActiveEditor = true;

                

                heatExchangerThrustDivisor = (float)GetHeatExchangerThrustDivisor();

                radiusModifier = (heatExchangerThrustDivisor * 100.0).ToString("0.00") + "%";

                maxISP = (float)(Math.Sqrt((double)myAttachedReactor.CoreTemperature) * (PluginHelper.IspCoreTempMult + IspTempMultOffset) * GetIspPropellantModifier());

                var max_thrust_in_space = GetPowerTrustModifier() * GetHeatTrustModifier() * myAttachedReactor.MaximumThermalPower / maxISP / PluginHelper.GravityConstant * heatExchangerThrustDivisor;

                var final_max_thrust_in_space = max_thrust_in_space * thrustPropellantMultiplier;

                var isp_in_space = heatExchangerThrustDivisor * maxISP;

                vacuumPerformance = final_max_thrust_in_space.ToString("0.0") + "kN @ " + isp_in_space.ToString("0.0") + "s";

                maxPressureTresholdAtKerbinSurface = exitArea * (float)GameConstants.EarthAthmospherePresureAtSeaLevel;

                var maxSurfaceTrust = Math.Max(max_thrust_in_space - (maxPressureTresholdAtKerbinSurface), 0.00001);

                var maxSurfaceISP = maxISP * (maxSurfaceTrust / max_thrust_in_space) * heatExchangerThrustDivisor;

                var final_max_surface_trust = maxSurfaceTrust * thrustPropellantMultiplier;

                surfacePerformance = final_max_surface_trust.ToString("0.0") + "kN @ " + maxSurfaceISP.ToString("0.0") + "s";
            }
            else
            {
                Fields["vacuumPerformance"].guiActiveEditor = false;
                Fields["radiusModifier"].guiActiveEditor = false;
                Fields["surfacePerformance"].guiActiveEditor = false;
            }
        }


        private double GetHeatExchangerThrustDivisor()
        {
            if (myAttachedReactor == null)
                return 0;

            if (myAttachedReactor.getRadius() == 0 || radius == 0)
                return 1;

            // scale down thrust if it's attached to the wrong sized reactor
            float heat_exchanger_thrust_divisor = radius > myAttachedReactor.getRadius()
                ? myAttachedReactor.getRadius() * myAttachedReactor.getRadius() / radius / radius
                //: radius * radius / myAttachedReactor.getRadius() / myAttachedReactor.getRadius();
                : normalizeFraction(radius / myAttachedReactor.getRadius(), 1f);

            return heat_exchanger_thrust_divisor;
        }

        private float normalizeFraction(float variable, float normalizer)
        {
            return (normalizer + variable) / (1f + normalizer);
        }

        public static ConfigNode[] getPropellantsHybrid() 
        {
            ConfigNode[] propellantlist = GameDatabase.Instance.GetConfigNodes("ATMOSPHERIC_NTR_PROPELLANT");
            ConfigNode[] propellantlist2 = GameDatabase.Instance.GetConfigNodes("BASIC_NTR_PROPELLANT");
            propellantlist = propellantlist.Concat(propellantlist2).ToArray();
            if (propellantlist == null || propellantlist2 == null) 
                PluginHelper.showInstallationErrorMessage();
            
            return propellantlist;
        }
	}
}
