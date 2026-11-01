
# 自动初始结构设计报告

Original F8 automatic product flow

此报告展示已保存证据，不重新计算或改变验收结论。
一阶可行、工作起点和中间阶段不等于原规格合格；已有镜头复用不计为新设计。
方案库快照只供查看和导出；精确续跑请打开原检查点并使用原计算版本。

## 来源与兼容性

- **运行记录**: autonomous-887e06acbedf43dbbe1cfa841a0c5f8a
- **证据指纹**: ef0af8ef2f4b17572a38596161213a6e916716a7482b33818363d50d8d0938f5
- **算法身份**: AutonomousTopologyV1
- **准备模式**: Cold
- **原任务指纹**: e8b26d98049f78dd1680825033e2996137f65f8a0b5d22996b624e6f45746f54
- **搜索计划指纹**: 32900ac579ed59da077a1e222e64595d8c00a0c82352cdc7769f61c739fed2af
- **版本 / 修订**: 1 / 19
- **计算版本**: 9cb73aa45d909214003215baf29f4738feb08d39b2a330052c5439b8a8aa4671
- **状态**: Completed

## 原验收规格

- **目标焦距 / F数**: 50 mm / F/8
- **焦距 / F数相对容差**: 0.02 / 0.02
- **RMS / 最大半径上限**: 50 / 150 µm
- **畸变 / 有效率**: 0.03 / 0.98
- **中心与边界检查**: 沿用记录的原验收协议，丢失光线不得视为合格

全部原目标、制造限制、采样、权限和预算如下，未作放宽。

```json
{
  "originalPreparationPlan": {
    "seeds": [
      {
        "schemaVersion": 1,
        "algorithm": "AutonomousTopologyV1",
        "name": "Descriptor Thick Delano",
        "conjugate": "infinite",
        "reference": {
          "id": "reference",
          "zMillimeters": 0
        },
        "stop": {
          "id": "stop",
          "zMillimeters": 0,
          "clearSemiDiameterMillimeters": 3.125,
          "mechanicalSemiDiameterMillimeters": null
        },
        "image": {
          "id": "image",
          "zMillimeters": 65,
          "protected": true
        },
        "elements": [
          {
            "id": "generated-0",
            "frontSurfaceId": "generated-0:front",
            "backSurfaceId": "generated-0:back",
            "frontZMillimeters": 4.665815730908137,
            "thicknessMillimeters": 1.8650041384258804,
            "material": "N-BK7",
            "front": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.00023870982706638931,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.02879297752319498,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-1",
            "frontSurfaceId": "generated-1:front",
            "backSurfaceId": "generated-1:back",
            "frontZMillimeters": 8.718216566014899,
            "thicknessMillimeters": 1.3308008771717166,
            "material": "N-F2",
            "front": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.018338739947622078,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.022977843213564213,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-2",
            "frontSurfaceId": "generated-2:front",
            "backSurfaceId": "generated-2:back",
            "frontZMillimeters": 12.082937704109014,
            "thicknessMillimeters": 0.7969023041352958,
            "material": "N-BK7",
            "front": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": 0.0012059765357857841,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": 0.013039225782330148,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-3",
            "frontSurfaceId": "generated-3:front",
            "backSurfaceId": "generated-3:back",
            "frontZMillimeters": 18.509241727768288,
            "thicknessMillimeters": 1.8398211940537266,
            "material": "N-F2",
            "front": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.017547467349528362,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.0357683860986254,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-4",
            "frontSurfaceId": "generated-4:front",
            "backSurfaceId": "generated-4:back",
            "frontZMillimeters": 28.320051665899996,
            "thicknessMillimeters": 0.9699475349030564,
            "material": "N-BK7",
            "front": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.035921160775212055,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.018521273070420532,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-5",
            "frontSurfaceId": "generated-5:front",
            "backSurfaceId": "generated-5:back",
            "frontZMillimeters": 32.429970285803485,
            "thicknessMillimeters": 1.455217664454608,
            "material": "N-F2",
            "front": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": 0.005277892001389233,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.014676578957237826,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-6",
            "frontSurfaceId": "generated-6:front",
            "backSurfaceId": "generated-6:back",
            "frontZMillimeters": 38.72887244749896,
            "thicknessMillimeters": 1.8982207791240668,
            "material": "N-BK7",
            "front": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.06805313848479408,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.07794012834304632,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-7",
            "frontSurfaceId": "generated-7:front",
            "backSurfaceId": "generated-7:back",
            "frontZMillimeters": 43.34328291499028,
            "thicknessMillimeters": 0.6909629586900508,
            "material": "N-F2",
            "front": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.056243685872124805,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.03697421557824045,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          }
        ],
        "fields": [
          {
            "label": "axis",
            "xAngleDegrees": 0,
            "yAngleDegrees": 0,
            "weight": 1
          },
          {
            "label": "edge",
            "xAngleDegrees": 0,
            "yAngleDegrees": 12.5,
            "weight": 1
          }
        ],
        "wavelengths": [
          {
            "label": "F",
            "nanometers": 486.1,
            "weight": 1,
            "isPrimary": false
          },
          {
            "label": "d",
            "nanometers": 587.6,
            "weight": 1,
            "isPrimary": true
          },
          {
            "label": "C",
            "nanometers": 656.3,
            "weight": 1,
            "isPrimary": false
          }
        ],
        "glassCatalogs": [],
        "rayAimingEnabled": true,
        "matchRefractiveIndexData": true,
        "temperatureCelsius": 20,
        "pressureAtmospheres": 1
      }
    ],
    "targets": {
      "effectiveFocalLengthMillimeters": 50,
      "fNumber": 8,
      "focalLengthRelativeTolerance": 0.02,
      "fNumberRelativeTolerance": 0.02,
      "maximumRmsRadiusMillimeters": 0.05,
      "maximumSpotRadiusMillimeters": 0.15,
      "maximumDistortionFraction": 0.03,
      "minimumValidRayFraction": 0.98,
      "minimumEdgeThicknessMillimeters": 0.1,
      "minimumEdgeAirGapMillimeters": 0.05,
      "maximumOpticalTrackMillimeters": 65,
      "maximumAbsoluteSurfaceSlope": 10
    },
    "constraints": {
      "minimumElementCount": 2,
      "maximumElementCount": 8,
      "minimumCenterThicknessMillimeters": 0.1,
      "minimumAirGapMillimeters": 0.05,
      "minimumBackFocusMillimeters": 15,
      "maximumMechanicalTrackMillimeters": 65,
      "allowTransitionElements": false,
      "allowedGlassNames": []
    },
    "options": {
      "seedPreparation": {
        "coldStart": {
          "version": 6,
          "exploreBackFocus": true,
          "backFocusSlackFractions": [
            0.75,
            0.875,
            0.9375
          ],
          "attemptsPerFraction": 3,
          "maximumGradientEvaluations": 600
        },
        "generation": {
          "feasibleLayouts": {
            "version": 1,
            "maximumLayouts": 3,
            "stopAfterElementCandidates": [
              0,
              4,
              8
            ]
          },
          "materializationVersion": 3,
          "version": 1,
          "method": "delano",
          "elements": [
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-BK7"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-F2"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-BK7"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-F2"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-BK7"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-F2"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-BK7"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-F2"
              ]
            }
          ],
          "stopAfterElement": 0,
          "clearSemiDiameterMillimeters": 5,
          "minimumThicknessMillimeters": 0.5,
          "maximumThicknessMillimeters": 2,
          "minimumBackFocusMillimeters": 15,
          "annealingProposals": 12
        },
        "version": 1,
        "maximumStarts": 4,
        "retainedSeeds": 2,
        "maximumFunctionEvaluations": 600,
        "maximumEvaluationsPerStart": 600,
        "timeLimit": "00:10:00",
        "maximumTracedRays": 10000000,
        "focusATolerance": 0.001
      },
      "checkStageFeasibility": true,
      "protectCompensationRayBudget": true,
      "expansion": {
        "enableInsertion": true,
        "enableDeletion": true,
        "enableGradualDeletion": true,
        "enableSplit": true,
        "enableMerge": true,
        "enableMaterial": true,
        "enableProfile": true,
        "materialAlternatives": [
          "N-BK7",
          "N-F2"
        ],
        "deletionScales": [
          0.7,
          0.4,
          0.2,
          0.1,
          0.02
        ],
        "directDeletionRecoveryMaximumFunctionEvaluations": null,
        "splitAirGapMillimeters": 0.05,
        "splitFirstFraction": 0.55,
        "maximumEvenPower": 8,
        "maximumAbsoluteConic": 2,
        "coefficientCoordinateSagMillimeters": 0.02
      },
      "axialCompensation": {
        "maximumDisplacementMillimeters": 0.1,
        "coordinateScaleMillimeters": 0.1,
        "elementIds": []
      },
      "maximumTrials": 1,
      "candidatesPerElementCount": 4,
      "branchesPerParent": 6,
      "maximumFunctionEvaluations": 1000,
      "maximumGradientEvaluations": 200,
      "maximumTracedRays": 2000000,
      "maximumStageTracedRays": 400000,
      "finalValidationRayReserve": 200000,
      "timeLimit": "00:05:00",
      "seed": 45,
      "curvatureBoundPerMillimeter": 0.08,
      "insertionMaterials": [
        "N-BK7"
      ],
      "recovery": {
        "maximumAcceptedSteps": 30,
        "maximumFunctionEvaluations": 400,
        "timeLimit": "00:01:00",
        "aScale": 0.01,
        "focalCScale": 0.01,
        "aTolerance": 0.0001,
        "focalCTolerance": 0.0001,
        "minimumChangeWeight": 1E-06,
        "relativeDifferenceStep": 6.055454452393343E-06,
        "relativeRankTolerance": 1E-08,
        "profileRadialSamples": 64
      },
      "local": {
        "transmissionFirst": true,
        "activeParameterLimit": 2,
        "transmissionPolicyVersion": 4,
        "requireDescent": true,
        "maximumUpdates": 8,
        "maximumFunctionEvaluations": 9,
        "maximumGradientEvaluations": 9,
        "timeLimit": "00:05:00",
        "learningRate": 0.01,
        "computeMode": "cpu",
        "sampling": {
          "includeBoundaryClearance": true,
          "includeBoundaryMaximum": true,
          "fieldsPerDirection": 3,
          "pupilRings": 4,
          "pupilArms": 16,
          "profileRadialSamples": 64
        },
        "performFinalAssessment": false
      },
      "full": {
        "transmissionFirst": true,
        "transmissionPolicyVersion": 4,
        "requireDescent": true,
        "maximumUpdates": 12,
        "maximumFunctionEvaluations": 13,
        "maximumGradientEvaluations": 13,
        "timeLimit": "00:05:00",
        "learningRate": 0.01,
        "computeMode": "cpu",
        "sampling": {
          "includeBoundaryClearance": true,
          "includeBoundaryMaximum": true,
          "fieldsPerDirection": 3,
          "pupilRings": 4,
          "pupilArms": 16,
          "profileRadialSamples": 64
        },
        "performFinalAssessment": false
      }
    }
  },
  "searchOrSourcePlan": {
    "seeds": [
      {
        "schemaVersion": 1,
        "algorithm": "AutonomousTopologyV1",
        "name": "Descriptor Thick Delano",
        "conjugate": "infinite",
        "reference": {
          "id": "reference",
          "zMillimeters": 0
        },
        "stop": {
          "id": "stop",
          "zMillimeters": 0,
          "clearSemiDiameterMillimeters": 3.125,
          "mechanicalSemiDiameterMillimeters": null
        },
        "image": {
          "id": "image",
          "zMillimeters": 65,
          "protected": true
        },
        "elements": [
          {
            "id": "generated-0",
            "frontSurfaceId": "generated-0:front",
            "backSurfaceId": "generated-0:back",
            "frontZMillimeters": 0.4223047073100896,
            "thicknessMillimeters": 1.5092972301049614,
            "material": "N-BK7",
            "front": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": -0.0726876276306224,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": -3.2E-05
                  },
                  {
                    "power": 6,
                    "coefficient": -1.0083865252042736E-08
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": -0.009883539727424088,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": 1.1981900888828898E-05
                  },
                  {
                    "power": 6,
                    "coefficient": 2.6269722961748997E-09
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-1",
            "frontSurfaceId": "generated-1:front",
            "backSurfaceId": "generated-1:back",
            "frontZMillimeters": 2.066084945674024,
            "thicknessMillimeters": 1.2917981028826735,
            "material": "N-F2",
            "front": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": -0.01263542855709463,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": -9.757698053560064E-06
                  },
                  {
                    "power": 6,
                    "coefficient": -1.983901688703775E-09
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": -0.006003579614740037,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": -4.114600700514348E-06
                  },
                  {
                    "power": 6,
                    "coefficient": 2.1876008077422332E-10
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-2",
            "frontSurfaceId": "generated-2:front",
            "backSurfaceId": "generated-2:back",
            "frontZMillimeters": 3.518042425678892,
            "thicknessMillimeters": 0.8247290339483304,
            "material": "N-BK7",
            "front": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": -0.0065900688541994405,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": 7.107536596782174E-06
                  },
                  {
                    "power": 6,
                    "coefficient": 1.5535006567562462E-10
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": -0.04139055022901689,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": 1.1133670444951489E-06
                  },
                  {
                    "power": 6,
                    "coefficient": -6.477052254847561E-10
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-3",
            "frontSurfaceId": "generated-3:front",
            "backSurfaceId": "generated-3:back",
            "frontZMillimeters": 4.4683009605703985,
            "thicknessMillimeters": 1.8943747718666257,
            "material": "N-F2",
            "front": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": -0.008532577379467202,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": 8.52169211963777E-06
                  },
                  {
                    "power": 6,
                    "coefficient": 6.475715180906723E-10
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": -0.03442831588678744,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": -6.941282521073873E-06
                  },
                  {
                    "power": 6,
                    "coefficient": -1.1929157074087796E-09
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-4",
            "frontSurfaceId": "generated-4:front",
            "backSurfaceId": "generated-4:back",
            "frontZMillimeters": 6.870544036711006,
            "thicknessMillimeters": 1.8751600183823296,
            "material": "N-BK7",
            "front": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": -0.0011138665733055055,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": 1.485869491211312E-05
                  },
                  {
                    "power": 6,
                    "coefficient": 1.9125005943622996E-09
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": -0.04384172315671054,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": -1.0224584248710071E-05
                  },
                  {
                    "power": 6,
                    "coefficient": -2.0824288641894362E-09
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-5",
            "frontSurfaceId": "generated-5:front",
            "backSurfaceId": "generated-5:back",
            "frontZMillimeters": 8.896697804583614,
            "thicknessMillimeters": 2.00479128654621,
            "material": "N-F2",
            "front": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": 0.003782797061837181,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": 6.5155355174493295E-06
                  },
                  {
                    "power": 6,
                    "coefficient": 1.5150458653696192E-09
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": -0.017469733482168688,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": -2.8021530999659084E-06
                  },
                  {
                    "power": 6,
                    "coefficient": -1.3798691514285023E-09
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-6",
            "frontSurfaceId": "generated-6:front",
            "backSurfaceId": "generated-6:back",
            "frontZMillimeters": 11.286895535835288,
            "thicknessMillimeters": 1.9343431897589163,
            "material": "N-BK7",
            "front": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": 0.006182213168578668,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": -5.093352478840003E-06
                  },
                  {
                    "power": 6,
                    "coefficient": 2.4081809979144466E-09
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": 0.014482451783158911,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": 5.8831481339419875E-06
                  },
                  {
                    "power": 6,
                    "coefficient": -6.896964524449965E-11
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "generated-7",
            "frontSurfaceId": "generated-7:front",
            "backSurfaceId": "generated-7:back",
            "frontZMillimeters": 13.713642584021656,
            "thicknessMillimeters": 1.5783654913711818,
            "material": "N-F2",
            "front": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": -0.02027508567062973,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": -5.3828485393541194E-06
                  },
                  {
                    "power": 6,
                    "coefficient": -6.546419073282702E-11
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "back": {
              "profile": {
                "kind": "evenAsphere",
                "curvaturePerMillimeter": 0.007836626096334155,
                "conic": 0,
                "maximumEvenPower": 6,
                "terms": [
                  {
                    "power": 4,
                    "coefficient": 3.2E-05
                  },
                  {
                    "power": 6,
                    "coefficient": 4.560368935351644E-09
                  }
                ]
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": 5
            },
            "protected": false,
            "transition": false
          }
        ],
        "fields": [
          {
            "label": "axis",
            "xAngleDegrees": 0,
            "yAngleDegrees": 0,
            "weight": 1
          },
          {
            "label": "edge",
            "xAngleDegrees": 0,
            "yAngleDegrees": 12.5,
            "weight": 1
          }
        ],
        "wavelengths": [
          {
            "label": "F",
            "nanometers": 486.1,
            "weight": 1,
            "isPrimary": false
          },
          {
            "label": "d",
            "nanometers": 587.6,
            "weight": 1,
            "isPrimary": true
          },
          {
            "label": "C",
            "nanometers": 656.3,
            "weight": 1,
            "isPrimary": false
          }
        ],
        "glassCatalogs": [],
        "rayAimingEnabled": true,
        "matchRefractiveIndexData": true,
        "temperatureCelsius": 20,
        "pressureAtmospheres": 1
      }
    ],
    "targets": {
      "effectiveFocalLengthMillimeters": 50,
      "fNumber": 8,
      "focalLengthRelativeTolerance": 0.02,
      "fNumberRelativeTolerance": 0.02,
      "maximumRmsRadiusMillimeters": 0.05,
      "maximumSpotRadiusMillimeters": 0.15,
      "maximumDistortionFraction": 0.03,
      "minimumValidRayFraction": 0.98,
      "minimumEdgeThicknessMillimeters": 0.1,
      "minimumEdgeAirGapMillimeters": 0.05,
      "maximumOpticalTrackMillimeters": 65,
      "maximumAbsoluteSurfaceSlope": 10
    },
    "constraints": {
      "minimumElementCount": 2,
      "maximumElementCount": 8,
      "minimumCenterThicknessMillimeters": 0.1,
      "minimumAirGapMillimeters": 0.05,
      "minimumBackFocusMillimeters": 15,
      "maximumMechanicalTrackMillimeters": 65,
      "allowTransitionElements": false,
      "allowedGlassNames": []
    },
    "options": {
      "seedPreparation": {
        "coldStart": {
          "version": 6,
          "exploreBackFocus": true,
          "backFocusSlackFractions": [
            0.75,
            0.875,
            0.9375
          ],
          "attemptsPerFraction": 3,
          "maximumGradientEvaluations": 600
        },
        "generation": {
          "feasibleLayouts": {
            "version": 1,
            "maximumLayouts": 3,
            "stopAfterElementCandidates": [
              0,
              4,
              8
            ]
          },
          "materializationVersion": 3,
          "version": 1,
          "method": "delano",
          "elements": [
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-BK7"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-F2"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-BK7"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-F2"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-BK7"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-F2"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-BK7"
              ]
            },
            {
              "kind": "S",
              "profile": "standard",
              "materials": [
                "N-F2"
              ]
            }
          ],
          "stopAfterElement": 0,
          "clearSemiDiameterMillimeters": 5,
          "minimumThicknessMillimeters": 0.5,
          "maximumThicknessMillimeters": 2,
          "minimumBackFocusMillimeters": 15,
          "annealingProposals": 12
        },
        "version": 1,
        "maximumStarts": 4,
        "retainedSeeds": 2,
        "maximumFunctionEvaluations": 600,
        "maximumEvaluationsPerStart": 600,
        "timeLimit": "00:10:00",
        "maximumTracedRays": 10000000,
        "focusATolerance": 0.001
      },
      "checkStageFeasibility": true,
      "protectCompensationRayBudget": true,
      "expansion": {
        "enableInsertion": true,
        "enableDeletion": true,
        "enableGradualDeletion": true,
        "enableSplit": true,
        "enableMerge": true,
        "enableMaterial": true,
        "enableProfile": true,
        "materialAlternatives": [
          "N-BK7",
          "N-F2"
        ],
        "deletionScales": [
          0.7,
          0.4,
          0.2,
          0.1,
          0.02
        ],
        "directDeletionRecoveryMaximumFunctionEvaluations": null,
        "splitAirGapMillimeters": 0.05,
        "splitFirstFraction": 0.55,
        "maximumEvenPower": 8,
        "maximumAbsoluteConic": 2,
        "coefficientCoordinateSagMillimeters": 0.02
      },
      "axialCompensation": {
        "maximumDisplacementMillimeters": 0.1,
        "coordinateScaleMillimeters": 0.1,
        "elementIds": []
      },
      "maximumTrials": 1,
      "candidatesPerElementCount": 4,
      "branchesPerParent": 6,
      "maximumFunctionEvaluations": 1000,
      "maximumGradientEvaluations": 200,
      "maximumTracedRays": 2000000,
      "maximumStageTracedRays": 400000,
      "finalValidationRayReserve": 200000,
      "timeLimit": "00:05:00",
      "seed": 45,
      "curvatureBoundPerMillimeter": 0.08,
      "insertionMaterials": [
        "N-BK7"
      ],
      "recovery": {
        "maximumAcceptedSteps": 30,
        "maximumFunctionEvaluations": 400,
        "timeLimit": "00:01:00",
        "aScale": 0.01,
        "focalCScale": 0.01,
        "aTolerance": 0.0001,
        "focalCTolerance": 0.0001,
        "minimumChangeWeight": 1E-06,
        "relativeDifferenceStep": 6.055454452393343E-06,
        "relativeRankTolerance": 1E-08,
        "profileRadialSamples": 64
      },
      "local": {
        "transmissionFirst": true,
        "activeParameterLimit": 2,
        "transmissionPolicyVersion": 4,
        "requireDescent": true,
        "maximumUpdates": 8,
        "maximumFunctionEvaluations": 9,
        "maximumGradientEvaluations": 9,
        "timeLimit": "00:05:00",
        "learningRate": 0.01,
        "computeMode": "cpu",
        "sampling": {
          "includeBoundaryClearance": true,
          "includeBoundaryMaximum": true,
          "fieldsPerDirection": 3,
          "pupilRings": 4,
          "pupilArms": 16,
          "profileRadialSamples": 64
        },
        "performFinalAssessment": false
      },
      "full": {
        "transmissionFirst": true,
        "transmissionPolicyVersion": 4,
        "requireDescent": true,
        "maximumUpdates": 12,
        "maximumFunctionEvaluations": 13,
        "maximumGradientEvaluations": 13,
        "timeLimit": "00:05:00",
        "learningRate": 0.01,
        "computeMode": "cpu",
        "sampling": {
          "includeBoundaryClearance": true,
          "includeBoundaryMaximum": true,
          "fieldsPerDirection": 3,
          "pupilRings": 4,
          "pupilArms": 16,
          "profileRadialSamples": 64
        },
        "performFinalAssessment": false
      }
    }
  }
}
```


## 预算与实际后端

准备和主搜索费用分列，阶段费用已包含在累计费用中，不得重复相加；未记录不代表零工作。

### 主搜索累计费用


```json
{
  "consumed": {
    "functionEvaluations": 515,
    "gradientEvaluations": 22,
    "validationEvaluations": 4,
    "matrixEvaluations": 437,
    "differenceEvaluations": 448,
    "jacobianBuilds": 16,
    "tracedRays": 216756,
    "geometryProbes": 986774,
    "elapsed": "00:00:26.8422992"
  },
  "reservation": null,
  "options": {
    "seedPreparation": {
      "coldStart": {
        "version": 6,
        "exploreBackFocus": true,
        "backFocusSlackFractions": [
          0.75,
          0.875,
          0.9375
        ],
        "attemptsPerFraction": 3,
        "maximumGradientEvaluations": 600
      },
      "generation": {
        "feasibleLayouts": {
          "version": 1,
          "maximumLayouts": 3,
          "stopAfterElementCandidates": [
            0,
            4,
            8
          ]
        },
        "materializationVersion": 3,
        "version": 1,
        "method": "delano",
        "elements": [
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-BK7"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-F2"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-BK7"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-F2"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-BK7"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-F2"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-BK7"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-F2"
            ]
          }
        ],
        "stopAfterElement": 0,
        "clearSemiDiameterMillimeters": 5,
        "minimumThicknessMillimeters": 0.5,
        "maximumThicknessMillimeters": 2,
        "minimumBackFocusMillimeters": 15,
        "annealingProposals": 12
      },
      "version": 1,
      "maximumStarts": 4,
      "retainedSeeds": 2,
      "maximumFunctionEvaluations": 600,
      "maximumEvaluationsPerStart": 600,
      "timeLimit": "00:10:00",
      "maximumTracedRays": 10000000,
      "focusATolerance": 0.001
    },
    "checkStageFeasibility": true,
    "protectCompensationRayBudget": true,
    "expansion": {
      "enableInsertion": true,
      "enableDeletion": true,
      "enableGradualDeletion": true,
      "enableSplit": true,
      "enableMerge": true,
      "enableMaterial": true,
      "enableProfile": true,
      "materialAlternatives": [
        "N-BK7",
        "N-F2"
      ],
      "deletionScales": [
        0.7,
        0.4,
        0.2,
        0.1,
        0.02
      ],
      "directDeletionRecoveryMaximumFunctionEvaluations": null,
      "splitAirGapMillimeters": 0.05,
      "splitFirstFraction": 0.55,
      "maximumEvenPower": 8,
      "maximumAbsoluteConic": 2,
      "coefficientCoordinateSagMillimeters": 0.02
    },
    "axialCompensation": {
      "maximumDisplacementMillimeters": 0.1,
      "coordinateScaleMillimeters": 0.1,
      "elementIds": []
    },
    "maximumTrials": 1,
    "candidatesPerElementCount": 4,
    "branchesPerParent": 6,
    "maximumFunctionEvaluations": 1000,
    "maximumGradientEvaluations": 200,
    "maximumTracedRays": 2000000,
    "maximumStageTracedRays": 400000,
    "finalValidationRayReserve": 200000,
    "timeLimit": "00:05:00",
    "seed": 45,
    "curvatureBoundPerMillimeter": 0.08,
    "insertionMaterials": [
      "N-BK7"
    ],
    "recovery": {
      "maximumAcceptedSteps": 30,
      "maximumFunctionEvaluations": 400,
      "timeLimit": "00:01:00",
      "aScale": 0.01,
      "focalCScale": 0.01,
      "aTolerance": 0.0001,
      "focalCTolerance": 0.0001,
      "minimumChangeWeight": 1E-06,
      "relativeDifferenceStep": 6.055454452393343E-06,
      "relativeRankTolerance": 1E-08,
      "profileRadialSamples": 64
    },
    "local": {
      "transmissionFirst": true,
      "activeParameterLimit": 2,
      "transmissionPolicyVersion": 4,
      "requireDescent": true,
      "maximumUpdates": 8,
      "maximumFunctionEvaluations": 9,
      "maximumGradientEvaluations": 9,
      "timeLimit": "00:05:00",
      "learningRate": 0.01,
      "computeMode": "cpu",
      "sampling": {
        "includeBoundaryClearance": true,
        "includeBoundaryMaximum": true,
        "fieldsPerDirection": 3,
        "pupilRings": 4,
        "pupilArms": 16,
        "profileRadialSamples": 64
      },
      "performFinalAssessment": false
    },
    "full": {
      "transmissionFirst": true,
      "transmissionPolicyVersion": 4,
      "requireDescent": true,
      "maximumUpdates": 12,
      "maximumFunctionEvaluations": 13,
      "maximumGradientEvaluations": 13,
      "timeLimit": "00:05:00",
      "learningRate": 0.01,
      "computeMode": "cpu",
      "sampling": {
        "includeBoundaryClearance": true,
        "includeBoundaryMaximum": true,
        "fieldsPerDirection": 3,
        "pupilRings": 4,
        "pupilArms": 16,
        "profileRadialSamples": 64
      },
      "performFinalAssessment": false
    }
  }
}
```


### 准备累计费用


```json
{
  "consumed": {
    "functionEvaluations": 189,
    "gradientEvaluations": 128,
    "validationEvaluations": 6,
    "matrixEvaluations": 132,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 430800,
    "geometryProbes": 379318,
    "elapsed": "00:00:58.2850304"
  },
  "options": {
    "coldStart": {
      "version": 6,
      "exploreBackFocus": true,
      "backFocusSlackFractions": [
        0.75,
        0.875,
        0.9375
      ],
      "attemptsPerFraction": 3,
      "maximumGradientEvaluations": 600
    },
    "generation": {
      "feasibleLayouts": {
        "version": 1,
        "maximumLayouts": 3,
        "stopAfterElementCandidates": [
          0,
          4,
          8
        ]
      },
      "materializationVersion": 3,
      "version": 1,
      "method": "delano",
      "elements": [
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-BK7"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-F2"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-BK7"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-F2"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-BK7"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-F2"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-BK7"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-F2"
          ]
        }
      ],
      "stopAfterElement": 0,
      "clearSemiDiameterMillimeters": 5,
      "minimumThicknessMillimeters": 0.5,
      "maximumThicknessMillimeters": 2,
      "minimumBackFocusMillimeters": 15,
      "annealingProposals": 12
    },
    "version": 1,
    "maximumStarts": 4,
    "retainedSeeds": 2,
    "maximumFunctionEvaluations": 600,
    "maximumEvaluationsPerStart": 600,
    "timeLimit": "00:10:00",
    "maximumTracedRays": 10000000,
    "focusATolerance": 0.001
  },
  "pendingPhase": null,
  "reservation": null,
  "inFlight": null,
  "persistenceElapsed": "00:00:01.7551538",
  "lostReservations": 0
}
```

实际设备只按已记录的执行证据报告，不能用所选设备代替实际后端。
- **trial-0**: cpu-forward-v1; device=CPU; fallback=unrecorded; GPU derivative rays=0; batches=0
- **trial-0**: cpu-forward-v1; device=CPU; fallback=unrecorded; GPU derivative rays=0; batches=0

## 候选与原规格验收


### main:seed-0

- **来源**: 自主搜索
- **父方案**: —
- **验收**: 原规格合格
- **结果代码**: 保留候选 [保留候选]
- **片数**: 8
- **机械参考 / 光阑 / 像面位置**: 0 / 0 / 65 mm
- **光阑净半口径 (mm)**: 3.125

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.4223047073100896 | N-BK7 | EvenAsphere | 5 | 5 | -0.0726876276306224 | 0 | r4=-3.2E-05; r6=-1.0083865252042736E-08 |
| generated-0:back | 1.931601937415051 | Air | EvenAsphere | 5 | 5 | -0.009883539727424088 | 0 | r4=1.1981900888828898E-05; r6=2.6269722961748997E-09 |
| generated-1:front | 2.066084945674024 | N-F2 | EvenAsphere | 5 | 5 | -0.01263542855709463 | 0 | r4=-9.757698053560064E-06; r6=-1.983901688703775E-09 |
| generated-1:back | 3.3578830485566975 | Air | EvenAsphere | 5 | 5 | -0.006003579614740037 | 0 | r4=-4.114600700514348E-06; r6=2.1876008077422332E-10 |
| generated-2:front | 3.518042425678892 | N-BK7 | EvenAsphere | 5 | 5 | -0.0065900688541994405 | 0 | r4=7.107536596782174E-06; r6=1.5535006567562462E-10 |
| generated-2:back | 4.342771459627222 | Air | EvenAsphere | 5 | 5 | -0.04139055022901689 | 0 | r4=1.1133670444951489E-06; r6=-6.477052254847561E-10 |
| generated-3:front | 4.4683009605703985 | N-F2 | EvenAsphere | 5 | 5 | -0.008532577379467202 | 0 | r4=8.52169211963777E-06; r6=6.475715180906723E-10 |
| generated-3:back | 6.362675732437024 | Air | EvenAsphere | 5 | 5 | -0.03442831588678744 | 0 | r4=-6.941282521073873E-06; r6=-1.1929157074087796E-09 |
| generated-4:front | 6.870544036711006 | N-BK7 | EvenAsphere | 5 | 5 | -0.0011138665733055055 | 0 | r4=1.485869491211312E-05; r6=1.9125005943622996E-09 |
| generated-4:back | 8.745704055093336 | Air | EvenAsphere | 5 | 5 | -0.04384172315671054 | 0 | r4=-1.0224584248710071E-05; r6=-2.0824288641894362E-09 |
| generated-5:front | 8.896697804583614 | N-F2 | EvenAsphere | 5 | 5 | 0.003782797061837181 | 0 | r4=6.5155355174493295E-06; r6=1.5150458653696192E-09 |
| generated-5:back | 10.901489091129823 | Air | EvenAsphere | 5 | 5 | -0.017469733482168688 | 0 | r4=-2.8021530999659084E-06; r6=-1.3798691514285023E-09 |
| generated-6:front | 11.286895535835288 | N-BK7 | EvenAsphere | 5 | 5 | 0.006182213168578668 | 0 | r4=-5.093352478840003E-06; r6=2.4081809979144466E-09 |
| generated-6:back | 13.221238725594205 | Air | EvenAsphere | 5 | 5 | 0.014482451783158911 | 0 | r4=5.8831481339419875E-06; r6=-6.896964524449965E-11 |
| generated-7:front | 13.713642584021656 | N-F2 | EvenAsphere | 5 | 5 | -0.02027508567062973 | 0 | r4=-5.3828485393541194E-06; r6=-6.546419073282702E-11 |
| generated-7:back | 15.292008075392838 | Air | EvenAsphere | 5 | 5 | 0.007836626096334155 | 0 | r4=3.2E-05; r6=4.560368935351644E-09 |
- **验收协议**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **实际焦距 / F数**: 50.968399607312655 mm / 8.154943937170025

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 49.915503519408425 | 135.13725385288254 | 960/960 | 99/99 | 0 |
| 2 | 49.9082497656426 | 136.5888739725526 | 960/960 | 99/99 | 0.001948536150955615 |
| 3 | 49.8865688115606 | 137.84871132422992 | 960/960 | 99/99 | 0.00779454662802377 |
| 4 | 49.85071057639239 | 138.91287163137943 | 960/960 | 99/99 | 0.017539236663393565 |
| 5 | 49.801121848245074 | 139.7761995939111 | 960/960 | 99/99 | 0.031184612169124003 |
| 6 | 49.73849260294846 | 140.43226238692876 | 960/960 | 99/99 | 0.0487334752937442 |
| 7 | 49.66382271843594 | 140.87332891991844 | 960/960 | 99/99 | 0.07018941917938396 |
| 8 | 49.57851094233508 | 141.13250973982525 | 960/960 | 99/99 | 0.09555681887029116 |
| 9 | 49.484468462752204 | 141.39429364868477 | 960/960 | 99/99 | 0.12484082310332478 |
| 10 | 49.384259859542674 | 141.6947463886763 | 960/960 | 99/99 | 0.1580473427179752 |
| 11 | 49.28127450324042 | 141.976820306675 | 960/960 | 99/99 | 0.19518303690447472 |
| 12 | 49.17993151904943 | 142.34979675618783 | 960/960 | 99/99 | 0.23625529822967847 |
| 13 | 49.08592107389825 | 142.7452870610772 | 960/960 | 99/99 | 0.281272235950599 |
| 14 | 49.00648374836 | 143.08789103771502 | 960/960 | 99/99 | 0.33024265620864013 |
| 15 | 48.950727806187565 | 143.3754385281816 | 960/960 | 99/99 | 0.38317604086207824 |
| 16 | 48.929980875272484 | 143.8681789091773 | 960/960 | 99/99 | 0.4400825237043126 |
| 17 | 48.95816749980999 | 144.37730636954473 | 960/960 | 99/99 | 0.5009728644191369 |
| 18 | 49.05219686454582 | 144.8670260896784 | 960/960 | 99/99 | 0.565858420180615 |
| 19 | 49.23233567342831 | 145.3361673611855 | 960/960 | 99/99 | 0.6347511140699766 |
| 20 | 49.52253026567333 | 145.7840916218078 | 960/960 | 99/99 | 0.7076634014645593 |
| 21 | 49.950631191209126 | 146.21085886726647 | 960/960 | 99/99 | 0.7846082326461877 |

### event:1

- **来源**: 阶段端点
- **父方案**: —
- **验收**: 原规格合格
- **结果代码**: Accepted [Accepted]
- **片数**: 8
- **机械参考 / 光阑 / 像面位置**: 0 / 0 / 65 mm
- **光阑净半口径 (mm)**: 3.125

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.4223047073100896 | N-BK7 | EvenAsphere | 5 | 5 | -0.0726876276306224 | 0 | r4=-3.2E-05; r6=-1.0083865252042736E-08 |
| generated-0:back | 1.931601937415051 | Air | EvenAsphere | 5 | 5 | -0.009883539727424088 | 0 | r4=1.1981900888828898E-05; r6=2.6269722961748997E-09 |
| generated-1:front | 2.066084945674024 | N-F2 | EvenAsphere | 5 | 5 | -0.01263542855709463 | 0 | r4=-9.757698053560064E-06; r6=-1.983901688703775E-09 |
| generated-1:back | 3.3578830485566975 | Air | EvenAsphere | 5 | 5 | -0.006003579614740037 | 0 | r4=-4.114600700514348E-06; r6=2.1876008077422332E-10 |
| generated-2:front | 3.518042425678892 | N-BK7 | EvenAsphere | 5 | 5 | -0.0065900688541994405 | 0 | r4=7.107536596782174E-06; r6=1.5535006567562462E-10 |
| generated-2:back | 4.342771459627222 | Air | EvenAsphere | 5 | 5 | -0.04139055022901689 | 0 | r4=1.1133670444951489E-06; r6=-6.477052254847561E-10 |
| generated-3:front | 4.4683009605703985 | N-F2 | EvenAsphere | 5 | 5 | -0.008532577379467202 | 0 | r4=8.52169211963777E-06; r6=6.475715180906723E-10 |
| generated-3:back | 6.362675732437024 | Air | EvenAsphere | 5 | 5 | -0.03442831588678744 | 0 | r4=-6.941282521073873E-06; r6=-1.1929157074087796E-09 |
| generated-4:front | 6.870544036711006 | N-BK7 | EvenAsphere | 5 | 5 | -0.0011138665733055055 | 0 | r4=1.485869491211312E-05; r6=1.9125005943622996E-09 |
| generated-4:back | 8.745704055093336 | Air | EvenAsphere | 5 | 5 | -0.04384172315671054 | 0 | r4=-1.0224584248710071E-05; r6=-2.0824288641894362E-09 |
| generated-5:front | 8.896697804583614 | N-F2 | EvenAsphere | 5 | 5 | 0.003782797061837181 | 0 | r4=6.5155355174493295E-06; r6=1.5150458653696192E-09 |
| generated-5:back | 10.901489091129823 | Air | EvenAsphere | 5 | 5 | -0.017469733482168688 | 0 | r4=-2.8021530999659084E-06; r6=-1.3798691514285023E-09 |
| generated-6:front | 11.286895535835288 | N-BK7 | EvenAsphere | 5 | 5 | 0.006182213168578668 | 0 | r4=-5.093352478840003E-06; r6=2.4081809979144466E-09 |
| generated-6:back | 13.221238725594205 | Air | EvenAsphere | 5 | 5 | 0.014482451783158911 | 0 | r4=5.8831481339419875E-06; r6=-6.896964524449965E-11 |
| generated-7:front | 13.713642584021656 | N-F2 | EvenAsphere | 5 | 5 | -0.02027508567062973 | 0 | r4=-5.3828485393541194E-06; r6=-6.546419073282702E-11 |
| generated-7:back | 15.292008075392838 | Air | EvenAsphere | 5 | 5 | 0.007836626096334155 | 0 | r4=3.2E-05; r6=4.560368935351644E-09 |
- **验收协议**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **实际焦距 / F数**: 50.968399607312655 mm / 8.154943937170025

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 49.915503519408425 | 135.13725385288254 | 960/960 | 99/99 | 0 |
| 2 | 49.9082497656426 | 136.5888739725526 | 960/960 | 99/99 | 0.001948536150955615 |
| 3 | 49.8865688115606 | 137.84871132422992 | 960/960 | 99/99 | 0.00779454662802377 |
| 4 | 49.85071057639239 | 138.91287163137943 | 960/960 | 99/99 | 0.017539236663393565 |
| 5 | 49.801121848245074 | 139.7761995939111 | 960/960 | 99/99 | 0.031184612169124003 |
| 6 | 49.73849260294846 | 140.43226238692876 | 960/960 | 99/99 | 0.0487334752937442 |
| 7 | 49.66382271843594 | 140.87332891991844 | 960/960 | 99/99 | 0.07018941917938396 |
| 8 | 49.57851094233508 | 141.13250973982525 | 960/960 | 99/99 | 0.09555681887029116 |
| 9 | 49.484468462752204 | 141.39429364868477 | 960/960 | 99/99 | 0.12484082310332478 |
| 10 | 49.384259859542674 | 141.6947463886763 | 960/960 | 99/99 | 0.1580473427179752 |
| 11 | 49.28127450324042 | 141.976820306675 | 960/960 | 99/99 | 0.19518303690447472 |
| 12 | 49.17993151904943 | 142.34979675618783 | 960/960 | 99/99 | 0.23625529822967847 |
| 13 | 49.08592107389825 | 142.7452870610772 | 960/960 | 99/99 | 0.281272235950599 |
| 14 | 49.00648374836 | 143.08789103771502 | 960/960 | 99/99 | 0.33024265620864013 |
| 15 | 48.950727806187565 | 143.3754385281816 | 960/960 | 99/99 | 0.38317604086207824 |
| 16 | 48.929980875272484 | 143.8681789091773 | 960/960 | 99/99 | 0.4400825237043126 |
| 17 | 48.95816749980999 | 144.37730636954473 | 960/960 | 99/99 | 0.5009728644191369 |
| 18 | 49.05219686454582 | 144.8670260896784 | 960/960 | 99/99 | 0.565858420180615 |
| 19 | 49.23233567342831 | 145.3361673611855 | 960/960 | 99/99 | 0.6347511140699766 |
| 20 | 49.52253026567333 | 145.7840916218078 | 960/960 | 99/99 | 0.7076634014645593 |
| 21 | 49.950631191209126 | 146.21085886726647 | 960/960 | 99/99 | 0.7846082326461877 |

### event:4

- **来源**: 阶段端点
- **父方案**: seed-0
- **验收**: 过渡结构，仅供研究
- **结果代码**: UnmetTargets [UnmetTargets]
- **片数**: 8
- **机械参考 / 光阑 / 像面位置**: 0 / 0 / 65 mm
- **光阑净半口径 (mm)**: 3.125

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.6486992918258339 | N-BK7 | EvenAsphere | 5 | 5 | -0.05088133934143568 | 0 | r4=-2.24E-05; r6=-7.058705676429915E-09 |
| generated-0:back | 1.7052073528993068 | Air | EvenAsphere | 5 | 5 | -0.0069184778091968616 | 0 | r4=8.387330622180229E-06; r6=1.8388806073224296E-09 |
| generated-1:front | 2.066084945674024 | N-F2 | EvenAsphere | 5 | 5 | -0.016776527892707078 | 0 | r4=-9.757698053560064E-06; r6=-1.983901688703775E-09 |
| generated-1:back | 3.3578830485566975 | Air | EvenAsphere | 5 | 5 | -0.002365366074432582 | 0 | r4=-4.114600700514348E-06; r6=2.1876008077422332E-10 |
| generated-2:front | 3.518042425678892 | N-BK7 | EvenAsphere | 5 | 5 | -0.009533805411431954 | 0 | r4=7.107536596782174E-06; r6=1.5535006567562462E-10 |
| generated-2:back | 4.342771459627222 | Air | EvenAsphere | 5 | 5 | -0.038740083828983286 | 0 | r4=1.1133670444951489E-06; r6=-6.477052254847561E-10 |
| generated-3:front | 4.4683009605703985 | N-F2 | EvenAsphere | 5 | 5 | -0.011613378059344868 | 0 | r4=8.52169211963777E-06; r6=6.475715180906723E-10 |
| generated-3:back | 6.362675732437024 | Air | EvenAsphere | 5 | 5 | -0.03224543839265472 | 0 | r4=-6.941282521073873E-06; r6=-1.1929157074087796E-09 |
| generated-4:front | 6.870544036711006 | N-BK7 | EvenAsphere | 5 | 5 | -0.0025622548742148853 | 0 | r4=1.485869491211312E-05; r6=1.9125005943622996E-09 |
| generated-4:back | 8.745704055093336 | Air | EvenAsphere | 5 | 5 | -0.04329502001254718 | 0 | r4=-1.0224584248710071E-05; r6=-2.0824288641894362E-09 |
| generated-5:front | 8.896697804583614 | N-F2 | EvenAsphere | 5 | 5 | 0.003263433698375649 | 0 | r4=6.5155355174493295E-06; r6=1.5150458653696192E-09 |
| generated-5:back | 10.901489091129823 | Air | EvenAsphere | 5 | 5 | -0.018038679877167316 | 0 | r4=-2.8021530999659084E-06; r6=-1.3798691514285023E-09 |
| generated-6:front | 11.286895535835288 | N-BK7 | EvenAsphere | 5 | 5 | 0.006923301927838397 | 0 | r4=-5.093352478840003E-06; r6=2.4081809979144466E-09 |
| generated-6:back | 13.221238725594205 | Air | EvenAsphere | 5 | 5 | 0.012920986823523717 | 0 | r4=5.8831481339419875E-06; r6=-6.896964524449965E-11 |
| generated-7:front | 13.713642584021656 | N-F2 | EvenAsphere | 5 | 5 | -0.018036320620880834 | 0 | r4=-5.3828485393541194E-06; r6=-6.546419073282702E-11 |
| generated-7:back | 15.292008075392838 | Air | EvenAsphere | 5 | 5 | 0.004857667579810936 | 0 | r4=3.2E-05; r6=4.560368935351644E-09 |

#### 阶段验收条件


```json
{
  "targets": {
    "effectiveFocalLengthMillimeters": 50,
    "fNumber": 8,
    "focalLengthRelativeTolerance": 0.02,
    "fNumberRelativeTolerance": 0.02,
    "maximumRmsRadiusMillimeters": 0.05,
    "maximumSpotRadiusMillimeters": 0.15,
    "maximumDistortionFraction": 0.03,
    "minimumValidRayFraction": 0.98,
    "minimumEdgeThicknessMillimeters": 0.1,
    "minimumEdgeAirGapMillimeters": 0.05,
    "maximumOpticalTrackMillimeters": 65,
    "maximumAbsoluteSurfaceSlope": 10
  },
  "structuralConstraints": {
    "minimumElementCount": 2,
    "maximumElementCount": 8,
    "minimumCenterThicknessMillimeters": 0.1,
    "minimumAirGapMillimeters": 0.05,
    "minimumBackFocusMillimeters": 15,
    "maximumMechanicalTrackMillimeters": 65,
    "allowTransitionElements": true,
    "allowedGlassNames": [],
    "transitionAllowance": {
      "elementId": "generated-0",
      "minimumCenterThicknessMillimeters": 0.06999999999999999,
      "minimumEdgeThicknessMillimeters": 0.06999999999999999
    }
  }
}
```

- **验收协议**: babe22d580b2939a5bfbcfe882f2f33407ec8fc5b5df7ba9cc6097d7822a0fcc
- **实际焦距 / F数**: 50.96753787201874 mm / 8.154806059522999

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 59.05771294565695 | 150.50403752120374 | 960/960 | 99/99 | 0 |
| 2 | 59.10688558109085 | 157.1084214416616 | 960/960 | 99/99 | 0.0018993623631337689 |
| 3 | 59.25450808855725 | 163.47878631350076 | 960/960 | 99/99 | 0.007597717012534941 |
| 4 | 59.500903568199554 | 169.61642172281555 | 960/960 | 99/99 | 0.01709586522213351 |
| 5 | 59.84664130833757 | 175.52120608984944 | 960/960 | 99/99 | 0.030395137753460526 |
| 6 | 60.29258228414391 | 181.19160031217606 | 960/960 | 99/99 | 0.047497387525694434 |
| 7 | 60.839941533378386 | 186.6246363392565 | 960/960 | 99/99 | 0.06840498041191322 |
| 8 | 61.490366170517476 | 191.81590049823293 | 960/960 | 99/99 | 0.093120780981327 |
| 9 | 62.24602754909964 | 196.7595117599048 | 960/960 | 99/99 | 0.12164813767219711 |
| 10 | 63.109725892082956 | 201.4480945277004 | 960/960 | 99/99 | 0.15399086389324546 |
| 11 | 64.08500555117081 | 205.8727461550811 | 960/960 | 99/99 | 0.19015321480628744 |
| 12 | 65.17627893310085 | 210.02299885129983 | 960/960 | 99/99 | 0.23013986280683665 |
| 13 | 66.38895701036701 | 213.88677574120507 | 960/960 | 99/99 | 0.2739558682213663 |
| 14 | 67.7295842168213 | 217.45034113456097 | 960/960 | 99/99 | 0.32160664678970813 |
| 15 | 69.20597539033459 | 220.69824447786334 | 960/960 | 99/99 | 0.3730979337579035 |
| 16 | 70.82735228115757 | 223.61325788043374 | 960/960 | 99/99 | 0.4284357434657689 |
| 17 | 72.6044770113502 | 226.17630684766078 | 960/960 | 99/99 | 0.48762632548036944 |
| 18 | 74.54977975912955 | 228.36639385409896 | 960/960 | 99/99 | 0.5506761159529188 |
| 19 | 76.6774779343419 | 230.1605144864265 | 960/960 | 99/99 | 0.6175916835527198 |
| 20 | 79.00368419020863 | 231.53356542668922 | 960/960 | 99/99 | 0.6883796726236793 |
| 21 | 81.41080824061395 | 232.79199891906012 | 951/960 | 90/99 | 0.763046739125306 |
- **transition-element**: 1 / 0
- **field:0:rms**: 0.059057712945656954 / 0.05
- **field:0:maximum**: 0.15050403752120375 / 0.15
- **field:1:rms**: 0.05910688558109085 / 0.05
- **field:1:maximum**: 0.1571084214416616 / 0.15
- **field:2:rms**: 0.05925450808855725 / 0.05
- **field:2:maximum**: 0.16347878631350077 / 0.15
- **field:3:rms**: 0.05950090356819955 / 0.05
- **field:3:maximum**: 0.16961642172281555 / 0.15
- **field:4:rms**: 0.05984664130833757 / 0.05
- **field:4:maximum**: 0.17552120608984945 / 0.15
- **field:5:rms**: 0.060292582284143914 / 0.05
- **field:5:maximum**: 0.18119160031217607 / 0.15
- **field:6:rms**: 0.060839941533378385 / 0.05
- **field:6:maximum**: 0.18662463633925652 / 0.15
- **field:7:rms**: 0.06149036617051747 / 0.05
- **field:7:maximum**: 0.19181590049823294 / 0.15
- **field:8:rms**: 0.062246027549099636 / 0.05
- **field:8:maximum**: 0.1967595117599048 / 0.15
- **field:9:rms**: 0.06310972589208295 / 0.05
- **field:9:maximum**: 0.2014480945277004 / 0.15
- **field:10:rms**: 0.0640850055511708 / 0.05
- **field:10:maximum**: 0.2058727461550811 / 0.15
- **field:11:rms**: 0.06517627893310085 / 0.05
- **field:11:maximum**: 0.21002299885129982 / 0.15
- **field:12:rms**: 0.06638895701036701 / 0.05
- **field:12:maximum**: 0.21388677574120507 / 0.15
- **field:13:rms**: 0.06772958421682129 / 0.05
- **field:13:maximum**: 0.21745034113456096 / 0.15
- **field:14:rms**: 0.06920597539033459 / 0.05
- **field:14:maximum**: 0.22069824447786335 / 0.15
- **field:15:rms**: 0.07082735228115758 / 0.05
- **field:15:maximum**: 0.22361325788043374 / 0.15
- **field:16:rms**: 0.0726044770113502 / 0.05
- **field:16:maximum**: 0.22617630684766077 / 0.15
- **field:17:rms**: 0.07454977975912955 / 0.05
- **field:17:maximum**: 0.22836639385409896 / 0.15
- **field:18:rms**: 0.07667747793434189 / 0.05
- **field:18:maximum**: 0.2301605144864265 / 0.15
- **field:19:rms**: 0.07900368419020863 / 0.05
- **field:19:maximum**: 0.23153356542668924 / 0.15
- **field:20:check-lost**: 9 / 0
- **field:20:rms**: 0.08141080824061395 / 0.05
- **field:20:maximum**: 0.2327919989190601 / 0.15

### event:6

- **来源**: 阶段端点
- **父方案**: seed-0
- **验收**: 过渡结构，仅供研究
- **结果代码**: UnmetTargets [UnmetTargets]
- **片数**: 8
- **机械参考 / 光阑 / 像面位置**: 0 / 0 / 65 mm
- **光阑净半口径 (mm)**: 3.125

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.6486992918258339 | N-BK7 | EvenAsphere | 5 | 5 | -0.05088133934143568 | 0 | r4=-2.24E-05; r6=-7.058705676429915E-09 |
| generated-0:back | 1.7052073528993068 | Air | EvenAsphere | 5 | 5 | -0.0069184778091968616 | 0 | r4=8.387330622180229E-06; r6=1.8388806073224296E-09 |
| generated-1:front | 2.066084945674024 | N-F2 | EvenAsphere | 5 | 5 | -0.016776527892707078 | 0 | r4=-9.757698053560064E-06; r6=-1.983901688703775E-09 |
| generated-1:back | 3.3578830485566975 | Air | EvenAsphere | 5 | 5 | -0.002365366074432582 | 0 | r4=-4.114600700514348E-06; r6=2.1876008077422332E-10 |
| generated-2:front | 3.518042425678892 | N-BK7 | EvenAsphere | 5 | 5 | -0.009533805411431954 | 0 | r4=7.107536596782174E-06; r6=1.5535006567562462E-10 |
| generated-2:back | 4.342771459627222 | Air | EvenAsphere | 5 | 5 | -0.038740083828983286 | 0 | r4=1.1133670444951489E-06; r6=-6.477052254847561E-10 |
| generated-3:front | 4.4683009605703985 | N-F2 | EvenAsphere | 5 | 5 | -0.011613378059344868 | 0 | r4=8.52169211963777E-06; r6=6.475715180906723E-10 |
| generated-3:back | 6.362675732437024 | Air | EvenAsphere | 5 | 5 | -0.03224543839265472 | 0 | r4=-6.941282521073873E-06; r6=-1.1929157074087796E-09 |
| generated-4:front | 6.870544036711006 | N-BK7 | EvenAsphere | 5 | 5 | -0.0025622548742148853 | 0 | r4=1.485869491211312E-05; r6=1.9125005943622996E-09 |
| generated-4:back | 8.745704055093336 | Air | EvenAsphere | 5 | 5 | -0.04329502001254718 | 0 | r4=-1.0224584248710071E-05; r6=-2.0824288641894362E-09 |
| generated-5:front | 8.896697804583614 | N-F2 | EvenAsphere | 5 | 5 | 0.003263433698375649 | 0 | r4=6.5155355174493295E-06; r6=1.5150458653696192E-09 |
| generated-5:back | 10.901489091129823 | Air | EvenAsphere | 5 | 5 | -0.018038679877167316 | 0 | r4=-2.8021530999659084E-06; r6=-1.3798691514285023E-09 |
| generated-6:front | 11.286895535835288 | N-BK7 | EvenAsphere | 5 | 5 | 0.006923301927838397 | 0 | r4=-5.093352478840003E-06; r6=2.4081809979144466E-09 |
| generated-6:back | 13.221238725594205 | Air | EvenAsphere | 5 | 5 | 0.012920986823523717 | 0 | r4=5.8831481339419875E-06; r6=-6.896964524449965E-11 |
| generated-7:front | 13.713642584021656 | N-F2 | EvenAsphere | 5 | 5 | -0.018036320620880834 | 0 | r4=-5.3828485393541194E-06; r6=-6.546419073282702E-11 |
| generated-7:back | 15.292008075392838 | Air | EvenAsphere | 5 | 5 | 0.004857667579810936 | 0 | r4=3.2E-05; r6=4.560368935351644E-09 |

#### 阶段验收条件


```json
{
  "targets": {
    "effectiveFocalLengthMillimeters": 50,
    "fNumber": 8,
    "focalLengthRelativeTolerance": 0.02,
    "fNumberRelativeTolerance": 0.02,
    "maximumRmsRadiusMillimeters": 0.05,
    "maximumSpotRadiusMillimeters": 0.15,
    "maximumDistortionFraction": 0.03,
    "minimumValidRayFraction": 0.98,
    "minimumEdgeThicknessMillimeters": 0.1,
    "minimumEdgeAirGapMillimeters": 0.05,
    "maximumOpticalTrackMillimeters": 65,
    "maximumAbsoluteSurfaceSlope": 10
  },
  "structuralConstraints": {
    "minimumElementCount": 2,
    "maximumElementCount": 8,
    "minimumCenterThicknessMillimeters": 0.1,
    "minimumAirGapMillimeters": 0.05,
    "minimumBackFocusMillimeters": 15,
    "maximumMechanicalTrackMillimeters": 65,
    "allowTransitionElements": true,
    "allowedGlassNames": [],
    "transitionAllowance": {
      "elementId": "generated-0",
      "minimumCenterThicknessMillimeters": 0.06999999999999999,
      "minimumEdgeThicknessMillimeters": 0.06999999999999999
    }
  }
}
```

- **验收协议**: babe22d580b2939a5bfbcfe882f2f33407ec8fc5b5df7ba9cc6097d7822a0fcc
- **实际焦距 / F数**: 50.96753787201874 mm / 8.154806059522999

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 59.05771294565695 | 150.50403752120374 | 960/960 | 99/99 | 0 |
| 2 | 59.10688558109085 | 157.1084214416616 | 960/960 | 99/99 | 0.0018993623631337689 |
| 3 | 59.25450808855725 | 163.47878631350076 | 960/960 | 99/99 | 0.007597717012534941 |
| 4 | 59.500903568199554 | 169.61642172281555 | 960/960 | 99/99 | 0.01709586522213351 |
| 5 | 59.84664130833757 | 175.52120608984944 | 960/960 | 99/99 | 0.030395137753460526 |
| 6 | 60.29258228414391 | 181.19160031217606 | 960/960 | 99/99 | 0.047497387525694434 |
| 7 | 60.839941533378386 | 186.6246363392565 | 960/960 | 99/99 | 0.06840498041191322 |
| 8 | 61.490366170517476 | 191.81590049823293 | 960/960 | 99/99 | 0.093120780981327 |
| 9 | 62.24602754909964 | 196.7595117599048 | 960/960 | 99/99 | 0.12164813767219711 |
| 10 | 63.109725892082956 | 201.4480945277004 | 960/960 | 99/99 | 0.15399086389324546 |
| 11 | 64.08500555117081 | 205.8727461550811 | 960/960 | 99/99 | 0.19015321480628744 |
| 12 | 65.17627893310085 | 210.02299885129983 | 960/960 | 99/99 | 0.23013986280683665 |
| 13 | 66.38895701036701 | 213.88677574120507 | 960/960 | 99/99 | 0.2739558682213663 |
| 14 | 67.7295842168213 | 217.45034113456097 | 960/960 | 99/99 | 0.32160664678970813 |
| 15 | 69.20597539033459 | 220.69824447786334 | 960/960 | 99/99 | 0.3730979337579035 |
| 16 | 70.82735228115757 | 223.61325788043374 | 960/960 | 99/99 | 0.4284357434657689 |
| 17 | 72.6044770113502 | 226.17630684766078 | 960/960 | 99/99 | 0.48762632548036944 |
| 18 | 74.54977975912955 | 228.36639385409896 | 960/960 | 99/99 | 0.5506761159529188 |
| 19 | 76.6774779343419 | 230.1605144864265 | 960/960 | 99/99 | 0.6175916835527198 |
| 20 | 79.00368419020863 | 231.53356542668922 | 960/960 | 99/99 | 0.6883796726236793 |
| 21 | 81.41080824061395 | 232.79199891906012 | 951/960 | 90/99 | 0.763046739125306 |
- **transition-element**: 1 / 0
- **field:0:rms**: 0.059057712945656954 / 0.05
- **field:0:maximum**: 0.15050403752120375 / 0.15
- **field:1:rms**: 0.05910688558109085 / 0.05
- **field:1:maximum**: 0.1571084214416616 / 0.15
- **field:2:rms**: 0.05925450808855725 / 0.05
- **field:2:maximum**: 0.16347878631350077 / 0.15
- **field:3:rms**: 0.05950090356819955 / 0.05
- **field:3:maximum**: 0.16961642172281555 / 0.15
- **field:4:rms**: 0.05984664130833757 / 0.05
- **field:4:maximum**: 0.17552120608984945 / 0.15
- **field:5:rms**: 0.060292582284143914 / 0.05
- **field:5:maximum**: 0.18119160031217607 / 0.15
- **field:6:rms**: 0.060839941533378385 / 0.05
- **field:6:maximum**: 0.18662463633925652 / 0.15
- **field:7:rms**: 0.06149036617051747 / 0.05
- **field:7:maximum**: 0.19181590049823294 / 0.15
- **field:8:rms**: 0.062246027549099636 / 0.05
- **field:8:maximum**: 0.1967595117599048 / 0.15
- **field:9:rms**: 0.06310972589208295 / 0.05
- **field:9:maximum**: 0.2014480945277004 / 0.15
- **field:10:rms**: 0.0640850055511708 / 0.05
- **field:10:maximum**: 0.2058727461550811 / 0.15
- **field:11:rms**: 0.06517627893310085 / 0.05
- **field:11:maximum**: 0.21002299885129982 / 0.15
- **field:12:rms**: 0.06638895701036701 / 0.05
- **field:12:maximum**: 0.21388677574120507 / 0.15
- **field:13:rms**: 0.06772958421682129 / 0.05
- **field:13:maximum**: 0.21745034113456096 / 0.15
- **field:14:rms**: 0.06920597539033459 / 0.05
- **field:14:maximum**: 0.22069824447786335 / 0.15
- **field:15:rms**: 0.07082735228115758 / 0.05
- **field:15:maximum**: 0.22361325788043374 / 0.15
- **field:16:rms**: 0.0726044770113502 / 0.05
- **field:16:maximum**: 0.22617630684766077 / 0.15
- **field:17:rms**: 0.07454977975912955 / 0.05
- **field:17:maximum**: 0.22836639385409896 / 0.15
- **field:18:rms**: 0.07667747793434189 / 0.05
- **field:18:maximum**: 0.2301605144864265 / 0.15
- **field:19:rms**: 0.07900368419020863 / 0.05
- **field:19:maximum**: 0.23153356542668924 / 0.15
- **field:20:check-lost**: 9 / 0
- **field:20:rms**: 0.08141080824061395 / 0.05
- **field:20:maximum**: 0.2327919989190601 / 0.15

### event:8

- **来源**: 阶段端点
- **父方案**: seed-0
- **验收**: 过渡结构，仅供研究
- **结果代码**: TransitionRejected [TransitionRejected]
- **片数**: 8
- **机械参考 / 光阑 / 像面位置**: 0 / 0 / 65 mm
- **光阑净半口径 (mm)**: 3.125

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.6486992918258339 | N-BK7 | EvenAsphere | 5 | 5 | -0.05088133934143568 | 0 | r4=-2.24E-05; r6=-7.058705676429915E-09 |
| generated-0:back | 1.7052073528993068 | Air | EvenAsphere | 5 | 5 | -0.0069184778091968616 | 0 | r4=8.387330622180229E-06; r6=1.8388806073224296E-09 |
| generated-1:front | 2.0618493192227336 | N-F2 | EvenAsphere | 5 | 5 | -0.015991880688523613 | 0.0052559894525198245 | r4=-1.888361809898079E-05; r6=-8.965703868918161E-08 |
| generated-1:back | 3.3623064409312486 | Air | EvenAsphere | 5 | 5 | -0.003622429318144037 | -1.254999526278594E-05 | r4=3.288077503002404E-06; r6=-2.4570304686413903E-08 |
| generated-2:front | 3.5142577008073412 | N-BK7 | EvenAsphere | 5 | 5 | -0.008384465099152631 | 0.0006336694791415632 | r4=1.0809765106631697E-06; r6=3.21309016448465E-08 |
| generated-2:back | 4.34586245522187 | Air | EvenAsphere | 5 | 5 | -0.03864365684335631 | -0.043544007074608655 | r4=7.307189819633232E-06; r6=-5.631510896139487E-08 |
| generated-3:front | 4.464660295220396 | N-F2 | EvenAsphere | 5 | 5 | -0.010766875776265908 | 0.0012052628828832523 | r4=2.1865653141793276E-06; r6=1.2730345483422638E-07 |
| generated-3:back | 6.365902542146799 | Air | EvenAsphere | 5 | 5 | -0.03190848710297275 | -0.020850060021611928 | r4=-1.6754295411757644E-06; r6=-2.523965324994854E-07 |
| generated-4:front | 6.8674372899657 | N-BK7 | EvenAsphere | 5 | 5 | -0.001715570194159015 | 5.932344782032162E-06 | r4=1.1975376242779688E-05; r6=3.0023411286524995E-07 |
| generated-4:back | 8.748664808469202 | Air | EvenAsphere | 5 | 5 | -0.04301345526924635 | -0.02063000510860948 | r4=-7.713527581769124E-06; r6=-3.426377122098871E-07 |
| generated-5:front | 8.892994672527044 | N-F2 | EvenAsphere | 5 | 5 | 0.0034283462845723146 | -1.228250545052552E-05 | r4=3.599267252490329E-06; r6=4.128372820960801E-07 |
| generated-5:back | 10.904760097372687 | Air | EvenAsphere | 5 | 5 | -0.017697667496756383 | -0.0018352244884113204 | r4=-1.7247913558317057E-07; r6=-3.796246313585296E-07 |
| generated-6:front | 11.283459666361361 | N-BK7 | EvenAsphere | 5 | 5 | 0.00669406139193336 | -0.00010872985951139142 | r4=-7.765862276773673E-06; r6=2.6601473834592865E-07 |
| generated-6:back | 13.225188168438173 | Air | EvenAsphere | 5 | 5 | 0.013989353884921054 | 0.000986467654857326 | r4=9.708356060640921E-06; r6=-1.4073375234875366E-07 |
| generated-7:front | 13.710656410458789 | N-F2 | EvenAsphere | 5 | 5 | -0.01890011843556438 | 0.0031257474651436006 | r4=-9.819696293319962E-06; r6=1.604285266043771E-07 |
| generated-7:back | 15.288626838266532 | Air | EvenAsphere | 5 | 5 | 0.006435965719482751 | 8.742616669800014E-05 | r4=5.871346792172142E-05; r6=-2.828561624542622E-08 |

#### 阶段验收条件


```json
{
  "targets": {
    "effectiveFocalLengthMillimeters": 50,
    "fNumber": 8,
    "focalLengthRelativeTolerance": 0.02,
    "fNumberRelativeTolerance": 0.02,
    "maximumRmsRadiusMillimeters": 0.05,
    "maximumSpotRadiusMillimeters": 0.15,
    "maximumDistortionFraction": 0.03,
    "minimumValidRayFraction": 0.98,
    "minimumEdgeThicknessMillimeters": 0.1,
    "minimumEdgeAirGapMillimeters": 0.05,
    "maximumOpticalTrackMillimeters": 65,
    "maximumAbsoluteSurfaceSlope": 10
  },
  "structuralConstraints": {
    "minimumElementCount": 2,
    "maximumElementCount": 8,
    "minimumCenterThicknessMillimeters": 0.1,
    "minimumAirGapMillimeters": 0.05,
    "minimumBackFocusMillimeters": 15,
    "maximumMechanicalTrackMillimeters": 65,
    "allowTransitionElements": true,
    "allowedGlassNames": [],
    "transitionAllowance": {
      "elementId": "generated-0",
      "minimumCenterThicknessMillimeters": 0.06999999999999999,
      "minimumEdgeThicknessMillimeters": 0.06999999999999999
    }
  }
}
```

- **验收协议**: babe22d580b2939a5bfbcfe882f2f33407ec8fc5b5df7ba9cc6097d7822a0fcc
- **实际焦距 / F数**: 50.645835393172995 mm / 8.10333366290768

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 24.965529916501954 | 58.4509115928291 | 960/960 | 99/99 | 0 |
| 2 | 25.259951512790366 | 62.71299463647595 | 960/960 | 99/99 | 0.0008386193799248217 |
| 3 | 26.120008700554028 | 67.32040477120637 | 960/960 | 99/99 | 0.003355390680892943 |
| 4 | 27.483283133541615 | 72.2063643342139 | 960/960 | 99/99 | 0.007553057970675473 |
| 5 | 29.265005949138818 | 77.30844055520114 | 960/960 | 99/99 | 0.013436210007466648 |
| 6 | 31.375253758211237 | 82.56834429080229 | 960/960 | 99/99 | 0.0210113030723776 |
| 7 | 33.73046506842492 | 87.93175336076774 | 960/960 | 99/99 | 0.03028669425944502 |
| 8 | 36.258622932513035 | 93.34815986252387 | 960/960 | 99/99 | 0.0412726822775309 |
| 9 | 38.900187980292 | 98.77074104328187 | 960/960 | 99/99 | 0.05398156081984721 |
| 10 | 41.607016484459 | 104.1562532101512 | 960/960 | 99/99 | 0.06842768105617685 |
| 11 | 44.34070882602368 | 109.46494874786427 | 960/960 | 99/99 | 0.08462752420524718 |
| 12 | 47.0711012534449 | 114.66051595878301 | 960/960 | 99/99 | 0.1025997865117434 |
| 13 | 49.77517712522713 | 119.71004185019574 | 960/960 | 99/99 | 0.1223654763817609 |
| 14 | 52.4364693496066 | 124.58399817362587 | 960/960 | 99/99 | 0.1439480229690458 |
| 15 | 55.04495064729728 | 129.2562510090658 | 960/960 | 99/99 | 0.1673733992997803 |
| 16 | 57.59739216347659 | 133.70409453249545 | 960/960 | 99/99 | 0.1926702592891764 |
| 17 | 60.09817579515274 | 137.90830949328736 | 960/960 | 99/99 | 0.21987009008417 |
| 18 | 62.5605518549896 | 141.85324746951977 | 960/960 | 99/99 | 0.24900738213864607 |
| 19 | 65.00833008855425 | 151.0564364800544 | 960/960 | 99/99 | 0.2801198143049447 |
| 20 | 67.47796980912946 | 174.26895256162922 | 960/960 | 99/99 | 0.3132484618471909 |
| 21 | 70.02098564897095 | 200.6521114458568 | 960/960 | 99/99 | 0.34843802275482283 |
- **transition-element**: 1 / 0
- **field:13:rms**: 0.0524364693496066 / 0.05
- **field:14:rms**: 0.05504495064729728 / 0.05
- **field:15:rms**: 0.05759739216347659 / 0.05
- **field:16:rms**: 0.06009817579515274 / 0.05
- **field:17:rms**: 0.0625605518549896 / 0.05
- **field:18:rms**: 0.06500833008855425 / 0.05
- **field:18:maximum**: 0.1510564364800544 / 0.15
- **field:19:rms**: 0.06747796980912946 / 0.05
- **field:19:maximum**: 0.17426895256162922 / 0.15
- **field:20:rms**: 0.07002098564897095 / 0.05
- **field:20:maximum**: 0.20065211144585682 / 0.15

### construction:1

- **来源**: 构造起点
- **父方案**: —
- **验收**: 原规格未达标
- **结果代码**: OriginalAssessed [OriginalAssessed]
- **片数**: 8
- **机械参考 / 光阑 / 像面位置**: 0 / 0 / 65 mm
- **光阑净半口径 (mm)**: 3.125

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.9777644445917321 | N-BK7 | Standard | 5 | 5 | -0.06123892157533834 | 0 |  |
| generated-0:back | 2.950717393088152 | Air | Standard | 5 | 5 | -0.055347097617733286 | 0 |  |
| generated-1:front | 3.5721866660219943 | N-F2 | Standard | 5 | 5 | 7.353508075107836E-05 | 0 |  |
| generated-1:back | 4.868975756289951 | Air | Standard | 5 | 5 | -0.03503139680989672 | 0 |  |
| generated-2:front | 5.305552332581178 | N-BK7 | Standard | 5 | 5 | -0.03630664933284401 | 0 |  |
| generated-2:back | 7.083755736618853 | Air | Standard | 5 | 5 | 0.02838041456073252 | 0 |  |
| generated-3:front | 7.8620359731175835 | N-F2 | Standard | 5 | 5 | 0.03109376477976531 | 0 |  |
| generated-3:back | 8.39024499373942 | Air | Standard | 5 | 5 | 0.02927535615856357 | 0 |  |
| generated-4:front | 8.801579426536575 | N-BK7 | Standard | 5 | 5 | 0.050287994212631464 | 0 |  |
| generated-4:back | 9.868472645256855 | Air | Standard | 5 | 5 | 0.013345570464494897 | 0 |  |
| generated-5:front | 10.334085099857663 | N-F2 | Standard | 5 | 5 | -0.00602780590727408 | 0 |  |
| generated-5:back | 11.911046773434366 | Air | Standard | 5 | 5 | -0.03590680789955906 | 0 |  |
| generated-6:front | 12.778946777523437 | N-BK7 | Standard | 5 | 5 | -0.025131814407504435 | 0 |  |
| generated-6:back | 13.796278734572851 | Air | Standard | 5 | 5 | 0.009336163358879473 | 0 |  |
| generated-7:front | 14.88032100046899 | N-F2 | Standard | 5 | 5 | -0.03453281438594365 | 0 |  |
| generated-7:back | 15.414851185579654 | Air | Standard | 5 | 5 | -0.052712663612884764 | 0 |  |
- **验收协议**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **实际焦距 / F数**: 49.99999999999998 mm / 7.9999999999999964

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 107.77432360871865 | 280.3061776996834 | 960/960 | 99/99 | 0 |
| 2 | 108.05030798726898 | 290.0949563301035 | 960/960 | 99/99 | 0.003059360963480908 |
| 3 | 108.87935620725924 | 300.98372092758274 | 960/960 | 99/99 | 0.012241172331005998 |
| 4 | 110.26474598884387 | 313.00174475770535 | 960/960 | 99/99 | 0.02755663307867438 |
| 5 | 112.21191680443762 | 326.1824211786761 | 960/960 | 99/99 | 0.04902445340450464 |
| 6 | 114.728440478117 | 340.563456306211 | 960/960 | 99/99 | 0.07667092290485653 |
| 7 | 117.82399036949768 | 356.1870927051225 | 960/960 | 99/99 | 0.11053000665219966 |
| 8 | 121.51031734162382 | 373.10036641452183 | 960/960 | 99/99 | 0.15064346982444682 |
| 9 | 125.80124060451057 | 391.35540001120586 | 960/960 | 99/99 | 0.19706103172019204 |
| 10 | 130.7126604322999 | 411.00973485854644 | 960/960 | 99/99 | 0.24984055020431414 |
| 11 | 136.26259805540246 | 432.12670618632563 | 960/960 | 99/99 | 0.30904823783501384 |
| 12 | 142.47126615448886 | 454.775865210852 | 960/960 | 99/99 | 0.37475891116403115 |
| 13 | 149.3611716944057 | 479.03345314498137 | 960/960 | 99/99 | 0.44705627492805083 |
| 14 | 156.95725157347258 | 504.9829326800452 | 960/960 | 99/99 | 0.526033243125401 |
| 15 | 165.2870408312411 | 532.7155833650074 | 960/960 | 99/99 | 0.611792299243169 |
| 16 | 174.3808729455812 | 562.3311682785009 | 960/960 | 99/99 | 0.7044458982196556 |
| 17 | 184.2721119756327 | 593.9386805186259 | 960/960 | 99/99 | 0.8041169130477106 |
| 18 | 193.38050594188195 | 601.888727814034 | 951/960 | 86/99 | 0.9109391293114303 |
| 19 | 198.12187857494303 | 605.6709039741093 | 930/960 | 80/99 | 1.0250577913405137 |
| 20 | 203.43231179009038 | 601.6367710376969 | 911/960 | 78/99 | 1.1466302041226413 |
| 21 | 210.17636822725015 | 586.7337899200771 | 897/960 | 72/99 | 1.2758263956204452 |
- **field:0:rms**: 0.10777432360871865 / 0.05
- **field:0:maximum**: 0.2803061776996834 / 0.15
- **field:1:rms**: 0.10805030798726897 / 0.05
- **field:1:maximum**: 0.2900949563301035 / 0.15
- **field:2:rms**: 0.10887935620725923 / 0.05
- **field:2:maximum**: 0.30098372092758274 / 0.15
- **field:3:rms**: 0.11026474598884387 / 0.05
- **field:3:maximum**: 0.31300174475770537 / 0.15
- **field:4:rms**: 0.11221191680443762 / 0.05
- **field:4:maximum**: 0.32618242117867613 / 0.15
- **field:5:rms**: 0.11472844047811699 / 0.05
- **field:5:maximum**: 0.340563456306211 / 0.15
- **field:6:rms**: 0.11782399036949769 / 0.05
- **field:6:maximum**: 0.3561870927051225 / 0.15
- **field:7:rms**: 0.12151031734162382 / 0.05
- **field:7:maximum**: 0.3731003664145218 / 0.15
- **field:8:rms**: 0.12580124060451056 / 0.05
- **field:8:maximum**: 0.39135540001120583 / 0.15
- **field:9:rms**: 0.13071266043229993 / 0.05
- **field:9:maximum**: 0.4110097348585464 / 0.15
- **field:10:rms**: 0.13626259805540247 / 0.05
- **field:10:maximum**: 0.43212670618632565 / 0.15
- **field:11:rms**: 0.14247126615448885 / 0.05
- **field:11:maximum**: 0.454775865210852 / 0.15
- **field:12:rms**: 0.1493611716944057 / 0.05
- **field:12:maximum**: 0.47903345314498136 / 0.15
- **field:13:rms**: 0.15695725157347257 / 0.05
- **field:13:maximum**: 0.5049829326800452 / 0.15
- **field:14:rms**: 0.1652870408312411 / 0.05
- **field:14:maximum**: 0.5327155833650075 / 0.15
- **field:15:rms**: 0.1743808729455812 / 0.05
- **field:15:maximum**: 0.5623311682785008 / 0.15
- **field:16:rms**: 0.1842721119756327 / 0.05
- **field:16:maximum**: 0.593938680518626 / 0.15
- **field:17:check-lost**: 13 / 0
- **field:17:rms**: 0.19338050594188194 / 0.05
- **field:17:maximum**: 0.601888727814034 / 0.15
- **field:18:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:wave:0:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:wave:1:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:wave:2:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:check-lost**: 19 / 0
- **field:18:rms**: 0.19812187857494304 / 0.05
- **field:18:maximum**: 0.6056709039741093 / 0.15
- **field:19:lost-fraction**: 0.05104166666666665 / 0.020000000000000018
- **field:19:wave:0:lost-fraction**: 0.046875 / 0.020000000000000018
- **field:19:wave:1:lost-fraction**: 0.05312499999999998 / 0.020000000000000018
- **field:19:wave:2:lost-fraction**: 0.05312499999999998 / 0.020000000000000018
- **field:19:check-lost**: 21 / 0
- **field:19:rms**: 0.2034323117900904 / 0.05
- **field:19:maximum**: 0.6016367710376969 / 0.15
- **field:20:lost-fraction**: 0.06562500000000004 / 0.020000000000000018
- **field:20:wave:0:lost-fraction**: 0.06562500000000004 / 0.020000000000000018
- **field:20:wave:1:lost-fraction**: 0.06562500000000004 / 0.020000000000000018
- **field:20:wave:2:lost-fraction**: 0.06562500000000004 / 0.020000000000000018
- **field:20:check-lost**: 27 / 0
- **field:20:rms**: 0.21017636822725014 / 0.05
- **field:20:maximum**: 0.5867337899200771 / 0.15

### construction:2

- **来源**: 构造起点
- **父方案**: —
- **验收**: 原规格未达标
- **结果代码**: OriginalAssessed [OriginalAssessed]
- **片数**: 8
- **机械参考 / 光阑 / 像面位置**: 0 / 0 / 65 mm
- **光阑净半口径 (mm)**: 3.125

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.5223047073100896 | N-BK7 | Standard | 5 | 5 | -0.016298618371112683 | 0 |  |
| generated-0:back | 1.8318563273357462 | Air | Standard | 5 | 5 | -0.014472438463227844 | 0 |  |
| generated-1:front | 2.165853980575287 | N-F2 | Standard | 5 | 5 | -0.004747296561397991 | 0 |  |
| generated-1:back | 3.2583007195512668 | Air | Standard | 5 | 5 | -0.007895646874545938 | 0 |  |
| generated-2:front | 3.6176512974363924 | N-BK7 | Standard | 5 | 5 | 0.0009656873896203444 | 0 |  |
| generated-2:back | 4.242986178998442 | Air | Standard | 5 | 5 | -0.00420913245861854 | 0 |  |
| generated-3:front | 4.568098814644938 | N-F2 | Standard | 5 | 5 | -0.012097451400890361 | 0 |  |
| generated-3:back | 6.262675732437025 | Air | Standard | 5 | 5 | -0.00832825478240116 | 0 |  |
| generated-4:front | 6.970544036711006 | N-BK7 | Standard | 5 | 5 | -0.006976490420691322 | 0 |  |
| generated-4:back | 8.645704055093336 | Air | Standard | 5 | 5 | -0.01955291259687223 | 0 |  |
| generated-5:front | 8.996697804583613 | N-F2 | Standard | 5 | 5 | -0.01287965215770087 | 0 |  |
| generated-5:back | 10.801489091129824 | Air | Standard | 5 | 5 | -0.012010632781688941 | 0 |  |
| generated-6:front | 11.386895535835288 | N-BK7 | Standard | 5 | 5 | -0.01083472805182318 | 0 |  |
| generated-6:back | 13.126168369745427 | Air | Standard | 5 | 5 | -0.014110373258057637 | 0 |  |
| generated-7:front | 13.761133746430513 | N-F2 | Standard | 5 | 5 | -0.005404868197060827 | 0 |  |
| generated-7:back | 15.392008075392837 | Air | Standard | 5 | 5 | -0.023006445892353046 | 0 |  |
- **验收协议**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **实际焦距 / F数**: 49.99999999999998 mm / 7.9999999999999964

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 28.98065253781054 | 77.27027433975896 | 960/960 | 99/99 | 0 |
| 2 | 29.129488441617138 | 78.87263593693694 | 960/960 | 99/99 | 0.0030445445230872135 |
| 3 | 29.576490416851147 | 81.01068822515778 | 960/960 | 99/99 | 0.012179850626786644 |
| 4 | 30.323069943948116 | 83.6850577608943 | 960/960 | 99/99 | 0.02741093644364836 |
| 5 | 31.371360264487468 | 86.89641927497772 | 960/960 | 99/99 | 0.04874616729045117 |
| 6 | 32.72394498019184 | 90.64549297010149 | 960/960 | 99/99 | 0.07619725829871676 |
| 7 | 34.38357079818156 | 94.9330411614131 | 960/960 | 99/99 | 0.10977927807091718 |
| 8 | 36.352896949720545 | 99.75986424645988 | 960/960 | 99/99 | 0.14951065334043093 |
| 9 | 38.63431347471425 | 105.1267959896789 | 960/960 | 99/99 | 0.19541317459585003 |
| 10 | 41.22983744647117 | 111.03469810423228 | 960/960 | 99/99 | 0.24751200263435583 |
| 11 | 44.141078248279136 | 117.48445411275377 | 960/960 | 99/99 | 0.3058356759930119 |
| 12 | 47.369253511466056 | 124.47696246649275 | 960/960 | 99/99 | 0.37041611920161455 |
| 13 | 50.9152354960275 | 132.01312890096872 | 960/960 | 99/99 | 0.44128865179137167 |
| 14 | 54.7796107308233 | 140.09385800429897 | 960/960 | 99/99 | 0.518491997988093 |
| 15 | 58.9627407191583 | 148.7200439728804 | 960/960 | 99/99 | 0.6020682970071773 |
| 16 | 63.46481643462307 | 157.8925605277881 | 960/960 | 99/99 | 0.6920631138567752 |
| 17 | 68.28590319729638 | 167.61224996331947 | 960/960 | 99/99 | 0.7885254505527017 |
| 18 | 73.46157303290067 | 177.60096850800977 | 947/960 | 84/99 | 0.891507757632916 |
| 19 | 78.7277001891251 | 187.45968182628127 | 930/960 | 78/99 | 1.0010659458505309 |
| 20 | 84.33285683704355 | 197.3487310671942 | 909/960 | 78/99 | 1.117259397916593 |
| 21 | 90.30341539674768 | 208.24214850001255 | 897/960 | 72/99 | 1.2401509801509827 |
- **field:12:rms**: 0.0509152354960275 / 0.05
- **field:13:rms**: 0.0547796107308233 / 0.05
- **field:14:rms**: 0.058962740719158295 / 0.05
- **field:15:rms**: 0.06346481643462307 / 0.05
- **field:15:maximum**: 0.1578925605277881 / 0.15
- **field:16:rms**: 0.06828590319729638 / 0.05
- **field:16:maximum**: 0.16761224996331947 / 0.15
- **field:17:check-lost**: 15 / 0
- **field:17:rms**: 0.07346157303290067 / 0.05
- **field:17:maximum**: 0.17760096850800977 / 0.15
- **field:18:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:wave:0:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:wave:1:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:wave:2:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:check-lost**: 21 / 0
- **field:18:rms**: 0.0787277001891251 / 0.05
- **field:18:maximum**: 0.18745968182628125 / 0.15
- **field:19:lost-fraction**: 0.05312499999999998 / 0.020000000000000018
- **field:19:wave:0:lost-fraction**: 0.05312499999999998 / 0.020000000000000018
- **field:19:wave:1:lost-fraction**: 0.05312499999999998 / 0.020000000000000018
- **field:19:wave:2:lost-fraction**: 0.05312499999999998 / 0.020000000000000018
- **field:19:check-lost**: 21 / 0
- **field:19:rms**: 0.08433285683704356 / 0.05
- **field:19:maximum**: 0.1973487310671942 / 0.15
- **field:20:lost-fraction**: 0.06562500000000004 / 0.020000000000000018
- **field:20:wave:0:lost-fraction**: 0.06562500000000004 / 0.020000000000000018
- **field:20:wave:1:lost-fraction**: 0.06562500000000004 / 0.020000000000000018
- **field:20:wave:2:lost-fraction**: 0.06562500000000004 / 0.020000000000000018
- **field:20:check-lost**: 27 / 0
- **field:20:rms**: 0.09030341539674769 / 0.05
- **field:20:maximum**: 0.20824214850001255 / 0.15

### construction:3

- **来源**: 构造起点
- **父方案**: —
- **验收**: 原规格未达标
- **结果代码**: OriginalAssessed [OriginalAssessed]
- **片数**: 8
- **机械参考 / 光阑 / 像面位置**: 0 / 0 / 65 mm
- **光阑净半口径 (mm)**: 3.125

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.2582089029669606 | N-BK7 | Standard | 5 | 5 | 0.013370693940111707 | 0 |  |
| generated-0:back | 1.369806398121428 | Air | Standard | 5 | 5 | -0.0012581071317331095 | 0 |  |
| generated-1:front | 1.9036188509220082 | N-F2 | Standard | 5 | 5 | -0.022135700969556832 | 0 |  |
| generated-1:back | 3.522813201207078 | Air | Standard | 5 | 5 | 0.02795911047720978 | 0 |  |
| generated-2:front | 3.910952774412865 | N-BK7 | Standard | 5 | 5 | 0.04104685382023173 | 0 |  |
| generated-2:back | 5.0926593275810825 | Air | Standard | 5 | 5 | 0.02030095564198019 | 0 |  |
| generated-3:front | 5.851658137993445 | N-F2 | Standard | 5 | 5 | 0.014884494946703205 | 0 |  |
| generated-3:back | 7.536172122177398 | Air | Standard | 5 | 5 | 0.009941576274589246 | 0 |  |
| generated-4:front | 8.443270543432883 | N-BK7 | Standard | 5 | 5 | 0.04753484236885954 | 0 |  |
| generated-4:back | 9.17608031853997 | Air | Standard | 5 | 5 | 0.020270867083341214 | 0 |  |
| generated-5:front | 10.112662382658606 | N-F2 | Standard | 5 | 5 | -0.008371759642444155 | 0 |  |
| generated-5:back | 11.735707684563131 | Air | Standard | 5 | 5 | -0.028297877119511126 | 0 |  |
| generated-6:front | 12.476260763176734 | N-BK7 | Standard | 5 | 5 | -0.04927614384927446 | 0 |  |
| generated-6:back | 13.997727697989527 | Air | Standard | 5 | 5 | -0.03306289707187595 | 0 |  |
| generated-7:front | 14.329501646477278 | N-F2 | Standard | 5 | 5 | 0.01966416138413908 | 0 |  |
| generated-7:back | 15.093346375505586 | Air | Standard | 5 | 5 | 0.002747808517156074 | 0 |  |
- **验收协议**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **实际焦距 / F数**: 50.00000000000004 mm / 8.000000000000007

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 26.93127377568797 | 70.78695395939664 | 960/960 | 99/99 | 0 |
| 2 | 27.566456377950686 | 81.62795628197716 | 960/960 | 99/99 | 0.0049841812174659115 |
| 3 | 29.449150531194007 | 94.49300159991591 | 960/960 | 99/99 | 0.019942011321892505 |
| 4 | 32.52435725281968 | 109.40064310311514 | 960/960 | 99/99 | 0.04488935671338594 |
| 5 | 36.73230022222035 | 126.37294131501298 | 960/960 | 99/99 | 0.07985268486454236 |
| 6 | 42.02828169965331 | 145.435522852726 | 960/960 | 99/99 | 0.12486909951563338 |
| 7 | 48.388416980979365 | 166.61764913466382 | 960/960 | 99/99 | 0.17998638992115032 |
| 8 | 55.80662271394295 | 189.95229539808145 | 960/960 | 99/99 | 0.2452630941165689 |
| 9 | 64.28920568158958 | 215.47624044776015 | 960/960 | 99/99 | 0.3207685761695353 |
| 10 | 73.85021955111408 | 243.23016762258564 | 960/960 | 99/99 | 0.40658311736807057 |
| 11 | 84.50834942164919 | 273.2587775392084 | 960/960 | 99/99 | 0.502798021286054 |
| 12 | 96.28510047600047 | 305.6109132538252 | 960/960 | 99/99 | 0.6095157326573049 |
| 13 | 109.20386663833357 | 340.33969857489853 | 960/960 | 99/99 | 0.7268499699739205 |
| 14 | 123.28952670874817 | 377.50269036407946 | 960/960 | 99/99 | 0.8549258717155382 |
| 15 | 138.56833287946392 | 417.16204578085313 | 960/960 | 99/99 | 0.9938801560944717 |
| 16 | 155.06795084933324 | 459.38470556306487 | 960/960 | 99/99 | 1.143861294194937 |
| 17 | 170.3146989374914 | 469.60056080683324 | 948/960 | 84/99 | 1.30502969636182 |
| 18 | 183.07164216561188 | 484.78990834023074 | 924/960 | 78/99 | 1.4775579116813913 |
| 19 | 195.22096357031566 | 503.66226543687833 | 907/960 | 78/99 | 1.6616308403806328 |
| 20 | 211.24173861861627 | 522.7053915416456 | 895/960 | 72/99 | 1.8574459589561731 |
| 21 | 218.59619956184275 | 503.8152607877839 | 870/960 | 72/99 | 2.0652135578315938 |
- **field:6:maximum**: 0.16661764913466381 / 0.15
- **field:7:rms**: 0.05580662271394295 / 0.05
- **field:7:maximum**: 0.18995229539808145 / 0.15
- **field:8:rms**: 0.06428920568158958 / 0.05
- **field:8:maximum**: 0.21547624044776015 / 0.15
- **field:9:rms**: 0.07385021955111408 / 0.05
- **field:9:maximum**: 0.24323016762258565 / 0.15
- **field:10:rms**: 0.08450834942164918 / 0.05
- **field:10:maximum**: 0.27325877753920835 / 0.15
- **field:11:rms**: 0.09628510047600046 / 0.05
- **field:11:maximum**: 0.30561091325382517 / 0.15
- **field:12:rms**: 0.10920386663833356 / 0.05
- **field:12:maximum**: 0.34033969857489854 / 0.15
- **field:13:rms**: 0.12328952670874817 / 0.05
- **field:13:maximum**: 0.37750269036407946 / 0.15
- **field:14:rms**: 0.13856833287946393 / 0.05
- **field:14:maximum**: 0.41716204578085314 / 0.15
- **field:15:rms**: 0.15506795084933325 / 0.05
- **field:15:maximum**: 0.4593847055630649 / 0.15
- **field:16:check-lost**: 15 / 0
- **field:16:rms**: 0.1703146989374914 / 0.05
- **field:16:maximum**: 0.46960056080683327 / 0.15
- **field:17:lost-fraction**: 0.03749999999999998 / 0.020000000000000018
- **field:17:wave:0:lost-fraction**: 0.03749999999999998 / 0.020000000000000018
- **field:17:wave:1:lost-fraction**: 0.03749999999999998 / 0.020000000000000018
- **field:17:wave:2:lost-fraction**: 0.03749999999999998 / 0.020000000000000018
- **field:17:check-lost**: 21 / 0
- **field:17:rms**: 0.18307164216561186 / 0.05
- **field:17:maximum**: 0.4847899083402307 / 0.15
- **field:18:lost-fraction**: 0.055208333333333304 / 0.020000000000000018
- **field:18:wave:0:lost-fraction**: 0.05312499999999998 / 0.020000000000000018
- **field:18:wave:1:lost-fraction**: 0.05312499999999998 / 0.020000000000000018
- **field:18:wave:2:lost-fraction**: 0.059374999999999956 / 0.020000000000000018
- **field:18:check-lost**: 21 / 0
- **field:18:rms**: 0.19522096357031565 / 0.05
- **field:18:maximum**: 0.5036622654368783 / 0.15
- **field:19:lost-fraction**: 0.06770833333333337 / 0.020000000000000018
- **field:19:wave:0:lost-fraction**: 0.06562500000000004 / 0.020000000000000018
- **field:19:wave:1:lost-fraction**: 0.06874999999999998 / 0.020000000000000018
- **field:19:wave:2:lost-fraction**: 0.06874999999999998 / 0.020000000000000018
- **field:19:check-lost**: 27 / 0
- **field:19:rms**: 0.21124173861861625 / 0.05
- **field:19:maximum**: 0.5227053915416455 / 0.15
- **field:20:lost-fraction**: 0.09375 / 0.020000000000000018
- **field:20:wave:0:lost-fraction**: 0.09375 / 0.020000000000000018
- **field:20:wave:1:lost-fraction**: 0.09375 / 0.020000000000000018
- **field:20:wave:2:lost-fraction**: 0.09375 / 0.020000000000000018
- **field:20:check-lost**: 27 / 0
- **field:20:rms**: 0.21859619956184276 / 0.05
- **field:20:maximum**: 0.5038152607877839 / 0.15

### compensation:0

- **来源**: 补偿优化
- **父方案**: —
- **验收**: 原规格未达标
- **结果代码**: BudgetExhausted [BudgetExhausted]
- **片数**: 8
- **机械参考 / 光阑 / 像面位置**: 0 / 0 / 65 mm
- **光阑净半口径 (mm)**: 3.125

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.4223047073100896 | N-BK7 | Standard | 5 | 5 | -0.0727539760678057 | 0 |  |
| generated-0:back | 1.9318563273357463 | Air | Standard | 5 | 5 | -0.009353558213717092 | 0 |  |
| generated-1:front | 2.065853980575287 | N-F2 | Standard | 5 | 5 | -0.013015228552201786 | 0 |  |
| generated-1:back | 3.358300719551267 | Air | Standard | 5 | 5 | -0.0056817098503877325 | 0 |  |
| generated-2:front | 3.5176512974363923 | N-BK7 | Standard | 5 | 5 | -0.006984189607567894 | 0 |  |
| generated-2:back | 4.342986178998442 | Air | Standard | 5 | 5 | -0.041171828241516856 | 0 |  |
| generated-3:front | 4.4680988146449385 | N-F2 | Standard | 5 | 5 | -0.008761390435129175 | 0 |  |
| generated-3:back | 6.362675732437024 | Air | Standard | 5 | 5 | -0.03433731785648196 | 0 |  |
| generated-4:front | 6.87054403671101 | N-BK7 | Standard | 5 | 5 | -0.0012235962766148744 | 0 |  |
| generated-4:back | 8.745704055093336 | Air | Standard | 5 | 5 | -0.043828459769176854 | 0 |  |
| generated-5:front | 8.89669780458362 | N-F2 | Standard | 5 | 5 | 0.0038895961102698357 | 0 |  |
| generated-5:back | 10.90148909112981 | Air | Standard | 5 | 5 | -0.017690373260248395 | 0 |  |
| generated-6:front | 11.286895535835317 | N-BK7 | Standard | 5 | 5 | 0.006642982182211155 | 0 |  |
| generated-6:back | 13.226168367731766 | Air | Standard | 5 | 5 | 0.014323839979168786 | 0 |  |
| generated-7:front | 13.707922285350861 | N-F2 | Standard | 5 | 5 | -0.02014668909353571 | 0 |  |
| generated-7:back | 15.292008075392856 | Air | Standard | 5 | 5 | 0.006720714548599486 | 0 |  |
- **验收协议**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **实际焦距 / F数**: 50.96697323771302 mm / 8.154715718034083

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 42.2240082297651 | 120.94135823714215 | 960/960 | 99/99 | 0 |
| 2 | 42.29722933452303 | 121.11201123901438 | 960/960 | 99/99 | 0.002753027811909259 |
| 3 | 42.517396171855474 | 121.61872188517897 | 960/960 | 99/99 | 0.011013636484781352 |
| 4 | 42.88598939600404 | 122.46350008941032 | 960/960 | 99/99 | 0.024786402648842493 |
| 5 | 43.40537948501044 | 123.64817017144824 | 960/960 | 99/99 | 0.04407895708303776 |
| 6 | 44.078689118176584 | 125.17437546166255 | 960/960 | 99/99 | 0.06890198930131874 |
| 7 | 44.90961626064803 | 127.04358216761769 | 960/960 | 99/99 | 0.09926925396764802 |
| 8 | 45.902231809115946 | 129.257082514596 | 960/960 | 99/99 | 0.13519757912670016 |
| 9 | 47.06076704614437 | 131.81599717208138 | 960/960 | 99/99 | 0.17670687624194908 |
| 10 | 48.38940594209817 | 134.72127697689552 | 960/960 | 99/99 | 0.22382015202503266 |
| 11 | 49.892095588339586 | 137.97370396392682 | 960/960 | 99/99 | 0.2765635220404901 |
| 12 | 51.5723850344859 | 141.57389171433277 | 960/960 | 99/99 | 0.334966226065412 |
| 13 | 53.43329902068031 | 145.52228503134134 | 960/960 | 99/99 | 0.3990606451803577 |
| 14 | 55.47724912321979 | 149.8191589531075 | 960/960 | 99/99 | 0.4688823205652966 |
| 15 | 57.70598122088207 | 154.46461711267733 | 960/960 | 99/99 | 0.5444699739702564 |
| 16 | 60.12055535547941 | 159.45858945430837 | 960/960 | 99/99 | 0.6258655298262502 |
| 17 | 62.72135222022382 | 164.80082931696495 | 960/960 | 99/99 | 0.7131141389575302 |
| 18 | 65.50809967672402 | 170.49090989497807 | 960/960 | 99/99 | 0.8062642038520226 |
| 19 | 68.47991273064511 | 176.52822008772873 | 960/960 | 99/99 | 0.9053674054443905 |
| 20 | 71.63534105111287 | 182.91195975061214 | 960/960 | 99/99 | 1.0104787313550534 |
| 21 | 74.90076606822907 | 187.22567753043666 | 953/960 | 90/99 | 1.1216565055279952 |
- **field:11:rms**: 0.0515723850344859 / 0.05
- **field:12:rms**: 0.05343329902068031 / 0.05
- **field:13:rms**: 0.055477249123219795 / 0.05
- **field:14:rms**: 0.05770598122088207 / 0.05
- **field:14:maximum**: 0.15446461711267734 / 0.15
- **field:15:rms**: 0.06012055535547941 / 0.05
- **field:15:maximum**: 0.15945858945430835 / 0.15
- **field:16:rms**: 0.06272135222022382 / 0.05
- **field:16:maximum**: 0.16480082931696494 / 0.15
- **field:17:rms**: 0.06550809967672401 / 0.05
- **field:17:maximum**: 0.17049090989497806 / 0.15
- **field:18:rms**: 0.06847991273064512 / 0.05
- **field:18:maximum**: 0.17652822008772873 / 0.15
- **field:19:rms**: 0.07163534105111287 / 0.05
- **field:19:maximum**: 0.18291195975061214 / 0.15
- **field:20:check-lost**: 9 / 0
- **field:20:rms**: 0.07490076606822907 / 0.05
- **field:20:maximum**: 0.18722567753043665 / 0.15

### compensation:1

- **来源**: 补偿优化
- **父方案**: —
- **验收**: 原规格未达标
- **结果代码**: Converged [Converged]
- **片数**: 8
- **机械参考 / 光阑 / 像面位置**: 0 / 0 / 65 mm
- **光阑净半口径 (mm)**: 3.125

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.4223047073100896 | N-BK7 | EvenAsphere | 5 | 5 | -0.07268708393976803 | 0 | r4=-3.2E-05 |
| generated-0:back | 1.9318563273357463 | Air | EvenAsphere | 5 | 5 | -0.009883565186479306 | 0 | r4=1.1936750555783783E-05 |
| generated-1:front | 2.065853980575287 | N-F2 | EvenAsphere | 5 | 5 | -0.012635346612801834 | 0 | r4=-9.721371451352308E-06 |
| generated-1:back | 3.358300719551267 | Air | EvenAsphere | 5 | 5 | -0.006003637798369282 | 0 | r4=-4.133703572177471E-06 |
| generated-2:front | 3.5176512974363923 | N-BK7 | EvenAsphere | 5 | 5 | -0.00659005371016492 | 0 | r4=7.125947061481651E-06 |
| generated-2:back | 4.342986178998442 | Air | EvenAsphere | 5 | 5 | -0.04139066984125704 | 0 | r4=1.099233072402861E-06 |
| generated-3:front | 4.4680988146449385 | N-F2 | EvenAsphere | 5 | 5 | -0.008532518140849871 | 0 | r4=8.530284557065212E-06 |
| generated-3:back | 6.362675732437024 | Air | EvenAsphere | 5 | 5 | -0.034428402655285746 | 0 | r4=-6.941651486864527E-06 |
| generated-4:front | 6.870544036711006 | N-BK7 | EvenAsphere | 5 | 5 | -0.0011138862022038092 | 0 | r4=1.4851943837795802E-05 |
| generated-4:back | 8.745704055093336 | Air | EvenAsphere | 5 | 5 | -0.04384169467125613 | 0 | r4=-1.0212902277023412E-05 |
| generated-5:front | 8.896697804583614 | N-F2 | EvenAsphere | 5 | 5 | 0.0037828140192434612 | 0 | r4=6.5085601457006584E-06 |
| generated-5:back | 10.901489091129823 | Air | EvenAsphere | 5 | 5 | -0.017469708923416278 | 0 | r4=-2.7949698673625297E-06 |
| generated-6:front | 11.286895535835288 | N-BK7 | EvenAsphere | 5 | 5 | 0.006182111936538626 | 0 | r4=-5.1040758147783425E-06 |
| generated-6:back | 13.221238698920514 | Air | EvenAsphere | 5 | 5 | 0.014482506001003086 | 0 | r4=5.8832273177683365E-06 |
| generated-7:front | 13.713642723975546 | N-F2 | EvenAsphere | 5 | 5 | -0.020275159158561405 | 0 | r4=-5.380389788289528E-06 |
| generated-7:back | 15.292008075392838 | Air | EvenAsphere | 5 | 5 | 0.007836736709702755 | 0 | r4=3.2E-05 |
- **验收协议**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **实际焦距 / F数**: 50.96839958910285 mm / 8.154943934256456

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 50.06335217179974 | 135.4467902140873 | 960/960 | 99/99 | 0 |
| 2 | 50.055998511480155 | 136.94506367240976 | 960/960 | 99/99 | 0.0019480152937041686 |
| 3 | 50.03402203840108 | 138.24923381419097 | 960/960 | 99/99 | 0.007792473889407823 |
| 4 | 49.9976852837184 | 139.3554344441421 | 960/960 | 99/99 | 0.017534613129808054 |
| 5 | 49.947456085480106 | 140.2585447432303 | 960/960 | 99/99 | 0.03117649257893065 |
| 6 | 49.88405394155007 | 140.952172982852 | 960/960 | 99/99 | 0.048720989780061195 |
| 7 | 49.80851674824017 | 141.4286360428245 | 960/960 | 99/99 | 0.07017179529580986 |
| 8 | 49.722289755533154 | 141.70184277031137 | 960/960 | 99/99 | 0.09553340398309336 |
| 9 | 49.62733904941302 | 141.9666678300314 | 960/960 | 99/99 | 0.12481110723438758 |
| 10 | 49.52629228254745 | 142.25240451158524 | 960/960 | 99/99 | 0.1580109819244499 |
| 11 | 49.42260964290065 | 142.55564645388154 | 960/960 | 99/99 | 0.1951398772858457 |
| 12 | 49.32078807268533 | 142.88831510588736 | 960/960 | 99/99 | 0.23620540073483165 |
| 13 | 49.226601357921695 | 143.29901046659367 | 960/960 | 99/99 | 0.28121590157235066 |
| 14 | 49.14737767477968 | 143.65602590271692 | 960/960 | 99/99 | 0.33018045293993686 |
| 15 | 49.092314192733234 | 143.9572225336935 | 960/960 | 99/99 | 0.38310883081077407 |
| 16 | 49.072825005276094 | 144.36800101917004 | 960/960 | 99/99 | 0.44001149175264215 |
| 17 | 49.102913599702774 | 144.8848116273139 | 960/960 | 99/99 | 0.5008995480288534 |
| 18 | 49.199553958735144 | 145.38172861903354 | 960/960 | 99/99 | 0.5657847405430341 |
| 19 | 49.38305518791663 | 145.85759603585814 | 960/960 | 99/99 | 0.6346794087284233 |
| 20 | 49.677373895957224 | 146.31178306670188 | 960/960 | 99/99 | 0.7075964585361461 |
| 21 | 50.11032806302501 | 146.74434821523192 | 960/960 | 99/99 | 0.7845493267825228 |
- **field:0:rms**: 0.05006335217179974 / 0.05
- **field:1:rms**: 0.050055998511480154 / 0.05
- **field:2:rms**: 0.050034022038401085 / 0.05
- **field:20:rms**: 0.05011032806302501 / 0.05

### compensation:2

- **来源**: 补偿优化
- **父方案**: —
- **验收**: 原规格合格
- **结果代码**: Converged [Converged]
- **片数**: 8
- **机械参考 / 光阑 / 像面位置**: 0 / 0 / 65 mm
- **光阑净半口径 (mm)**: 3.125

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.4223047073100896 | N-BK7 | EvenAsphere | 5 | 5 | -0.0726876276306224 | 0 | r4=-3.2E-05; r6=-1.0083865252042736E-08 |
| generated-0:back | 1.931601937415051 | Air | EvenAsphere | 5 | 5 | -0.009883539727424088 | 0 | r4=1.1981900888828898E-05; r6=2.6269722961748997E-09 |
| generated-1:front | 2.066084945674024 | N-F2 | EvenAsphere | 5 | 5 | -0.01263542855709463 | 0 | r4=-9.757698053560064E-06; r6=-1.983901688703775E-09 |
| generated-1:back | 3.3578830485566975 | Air | EvenAsphere | 5 | 5 | -0.006003579614740037 | 0 | r4=-4.114600700514348E-06; r6=2.1876008077422332E-10 |
| generated-2:front | 3.518042425678892 | N-BK7 | EvenAsphere | 5 | 5 | -0.0065900688541994405 | 0 | r4=7.107536596782174E-06; r6=1.5535006567562462E-10 |
| generated-2:back | 4.342771459627222 | Air | EvenAsphere | 5 | 5 | -0.04139055022901689 | 0 | r4=1.1133670444951489E-06; r6=-6.477052254847561E-10 |
| generated-3:front | 4.4683009605703985 | N-F2 | EvenAsphere | 5 | 5 | -0.008532577379467202 | 0 | r4=8.52169211963777E-06; r6=6.475715180906723E-10 |
| generated-3:back | 6.362675732437024 | Air | EvenAsphere | 5 | 5 | -0.03442831588678744 | 0 | r4=-6.941282521073873E-06; r6=-1.1929157074087796E-09 |
| generated-4:front | 6.870544036711006 | N-BK7 | EvenAsphere | 5 | 5 | -0.0011138665733055055 | 0 | r4=1.485869491211312E-05; r6=1.9125005943622996E-09 |
| generated-4:back | 8.745704055093336 | Air | EvenAsphere | 5 | 5 | -0.04384172315671054 | 0 | r4=-1.0224584248710071E-05; r6=-2.0824288641894362E-09 |
| generated-5:front | 8.896697804583614 | N-F2 | EvenAsphere | 5 | 5 | 0.003782797061837181 | 0 | r4=6.5155355174493295E-06; r6=1.5150458653696192E-09 |
| generated-5:back | 10.901489091129823 | Air | EvenAsphere | 5 | 5 | -0.017469733482168688 | 0 | r4=-2.8021530999659084E-06; r6=-1.3798691514285023E-09 |
| generated-6:front | 11.286895535835288 | N-BK7 | EvenAsphere | 5 | 5 | 0.006182213168578668 | 0 | r4=-5.093352478840003E-06; r6=2.4081809979144466E-09 |
| generated-6:back | 13.221238725594205 | Air | EvenAsphere | 5 | 5 | 0.014482451783158911 | 0 | r4=5.8831481339419875E-06; r6=-6.896964524449965E-11 |
| generated-7:front | 13.713642584021656 | N-F2 | EvenAsphere | 5 | 5 | -0.02027508567062973 | 0 | r4=-5.3828485393541194E-06; r6=-6.546419073282702E-11 |
| generated-7:back | 15.292008075392838 | Air | EvenAsphere | 5 | 5 | 0.007836626096334155 | 0 | r4=3.2E-05; r6=4.560368935351644E-09 |
- **验收协议**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **实际焦距 / F数**: 50.968399607312655 mm / 8.154943937170025

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 49.915503519408425 | 135.13725385288254 | 960/960 | 99/99 | 0 |
| 2 | 49.9082497656426 | 136.5888739725526 | 960/960 | 99/99 | 0.001948536150955615 |
| 3 | 49.8865688115606 | 137.84871132422992 | 960/960 | 99/99 | 0.00779454662802377 |
| 4 | 49.85071057639239 | 138.91287163137943 | 960/960 | 99/99 | 0.017539236663393565 |
| 5 | 49.801121848245074 | 139.7761995939111 | 960/960 | 99/99 | 0.031184612169124003 |
| 6 | 49.73849260294846 | 140.43226238692876 | 960/960 | 99/99 | 0.0487334752937442 |
| 7 | 49.66382271843594 | 140.87332891991844 | 960/960 | 99/99 | 0.07018941917938396 |
| 8 | 49.57851094233508 | 141.13250973982525 | 960/960 | 99/99 | 0.09555681887029116 |
| 9 | 49.484468462752204 | 141.39429364868477 | 960/960 | 99/99 | 0.12484082310332478 |
| 10 | 49.384259859542674 | 141.6947463886763 | 960/960 | 99/99 | 0.1580473427179752 |
| 11 | 49.28127450324042 | 141.976820306675 | 960/960 | 99/99 | 0.19518303690447472 |
| 12 | 49.17993151904943 | 142.34979675618783 | 960/960 | 99/99 | 0.23625529822967847 |
| 13 | 49.08592107389825 | 142.7452870610772 | 960/960 | 99/99 | 0.281272235950599 |
| 14 | 49.00648374836 | 143.08789103771502 | 960/960 | 99/99 | 0.33024265620864013 |
| 15 | 48.950727806187565 | 143.3754385281816 | 960/960 | 99/99 | 0.38317604086207824 |
| 16 | 48.929980875272484 | 143.8681789091773 | 960/960 | 99/99 | 0.4400825237043126 |
| 17 | 48.95816749980999 | 144.37730636954473 | 960/960 | 99/99 | 0.5009728644191369 |
| 18 | 49.05219686454582 | 144.8670260896784 | 960/960 | 99/99 | 0.565858420180615 |
| 19 | 49.23233567342831 | 145.3361673611855 | 960/960 | 99/99 | 0.6347511140699766 |
| 20 | 49.52253026567333 | 145.7840916218078 | 960/960 | 99/99 | 0.7076634014645593 |
| 21 | 49.950631191209126 | 146.21085886726647 | 960/960 | 99/99 | 0.7846082326461877 |

### cold:final

- **来源**: 冷启动终点
- **父方案**: —
- **验收**: 原规格合格
- **结果代码**: Completed [Completed]
- **片数**: 8
- **机械参考 / 光阑 / 像面位置**: 0 / 0 / 65 mm
- **光阑净半口径 (mm)**: 3.125

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.4223047073100896 | N-BK7 | EvenAsphere | 5 | 5 | -0.0726876276306224 | 0 | r4=-3.2E-05; r6=-1.0083865252042736E-08 |
| generated-0:back | 1.931601937415051 | Air | EvenAsphere | 5 | 5 | -0.009883539727424088 | 0 | r4=1.1981900888828898E-05; r6=2.6269722961748997E-09 |
| generated-1:front | 2.066084945674024 | N-F2 | EvenAsphere | 5 | 5 | -0.01263542855709463 | 0 | r4=-9.757698053560064E-06; r6=-1.983901688703775E-09 |
| generated-1:back | 3.3578830485566975 | Air | EvenAsphere | 5 | 5 | -0.006003579614740037 | 0 | r4=-4.114600700514348E-06; r6=2.1876008077422332E-10 |
| generated-2:front | 3.518042425678892 | N-BK7 | EvenAsphere | 5 | 5 | -0.0065900688541994405 | 0 | r4=7.107536596782174E-06; r6=1.5535006567562462E-10 |
| generated-2:back | 4.342771459627222 | Air | EvenAsphere | 5 | 5 | -0.04139055022901689 | 0 | r4=1.1133670444951489E-06; r6=-6.477052254847561E-10 |
| generated-3:front | 4.4683009605703985 | N-F2 | EvenAsphere | 5 | 5 | -0.008532577379467202 | 0 | r4=8.52169211963777E-06; r6=6.475715180906723E-10 |
| generated-3:back | 6.362675732437024 | Air | EvenAsphere | 5 | 5 | -0.03442831588678744 | 0 | r4=-6.941282521073873E-06; r6=-1.1929157074087796E-09 |
| generated-4:front | 6.870544036711006 | N-BK7 | EvenAsphere | 5 | 5 | -0.0011138665733055055 | 0 | r4=1.485869491211312E-05; r6=1.9125005943622996E-09 |
| generated-4:back | 8.745704055093336 | Air | EvenAsphere | 5 | 5 | -0.04384172315671054 | 0 | r4=-1.0224584248710071E-05; r6=-2.0824288641894362E-09 |
| generated-5:front | 8.896697804583614 | N-F2 | EvenAsphere | 5 | 5 | 0.003782797061837181 | 0 | r4=6.5155355174493295E-06; r6=1.5150458653696192E-09 |
| generated-5:back | 10.901489091129823 | Air | EvenAsphere | 5 | 5 | -0.017469733482168688 | 0 | r4=-2.8021530999659084E-06; r6=-1.3798691514285023E-09 |
| generated-6:front | 11.286895535835288 | N-BK7 | EvenAsphere | 5 | 5 | 0.006182213168578668 | 0 | r4=-5.093352478840003E-06; r6=2.4081809979144466E-09 |
| generated-6:back | 13.221238725594205 | Air | EvenAsphere | 5 | 5 | 0.014482451783158911 | 0 | r4=5.8831481339419875E-06; r6=-6.896964524449965E-11 |
| generated-7:front | 13.713642584021656 | N-F2 | EvenAsphere | 5 | 5 | -0.02027508567062973 | 0 | r4=-5.3828485393541194E-06; r6=-6.546419073282702E-11 |
| generated-7:back | 15.292008075392838 | Air | EvenAsphere | 5 | 5 | 0.007836626096334155 | 0 | r4=3.2E-05; r6=4.560368935351644E-09 |
- **验收协议**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **实际焦距 / F数**: 50.968399607312655 mm / 8.154943937170025

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 49.915503519408425 | 135.13725385288254 | 960/960 | 99/99 | 0 |
| 2 | 49.9082497656426 | 136.5888739725526 | 960/960 | 99/99 | 0.001948536150955615 |
| 3 | 49.8865688115606 | 137.84871132422992 | 960/960 | 99/99 | 0.00779454662802377 |
| 4 | 49.85071057639239 | 138.91287163137943 | 960/960 | 99/99 | 0.017539236663393565 |
| 5 | 49.801121848245074 | 139.7761995939111 | 960/960 | 99/99 | 0.031184612169124003 |
| 6 | 49.73849260294846 | 140.43226238692876 | 960/960 | 99/99 | 0.0487334752937442 |
| 7 | 49.66382271843594 | 140.87332891991844 | 960/960 | 99/99 | 0.07018941917938396 |
| 8 | 49.57851094233508 | 141.13250973982525 | 960/960 | 99/99 | 0.09555681887029116 |
| 9 | 49.484468462752204 | 141.39429364868477 | 960/960 | 99/99 | 0.12484082310332478 |
| 10 | 49.384259859542674 | 141.6947463886763 | 960/960 | 99/99 | 0.1580473427179752 |
| 11 | 49.28127450324042 | 141.976820306675 | 960/960 | 99/99 | 0.19518303690447472 |
| 12 | 49.17993151904943 | 142.34979675618783 | 960/960 | 99/99 | 0.23625529822967847 |
| 13 | 49.08592107389825 | 142.7452870610772 | 960/960 | 99/99 | 0.281272235950599 |
| 14 | 49.00648374836 | 143.08789103771502 | 960/960 | 99/99 | 0.33024265620864013 |
| 15 | 48.950727806187565 | 143.3754385281816 | 960/960 | 99/99 | 0.38317604086207824 |
| 16 | 48.929980875272484 | 143.8681789091773 | 960/960 | 99/99 | 0.4400825237043126 |
| 17 | 48.95816749980999 | 144.37730636954473 | 960/960 | 99/99 | 0.5009728644191369 |
| 18 | 49.05219686454582 | 144.8670260896784 | 960/960 | 99/99 | 0.565858420180615 |
| 19 | 49.23233567342831 | 145.3361673611855 | 960/960 | 99/99 | 0.6347511140699766 |
| 20 | 49.52253026567333 | 145.7840916218078 | 960/960 | 99/99 | 0.7076634014645593 |
| 21 | 49.950631191209126 | 146.21085886726647 | 960/960 | 99/99 | 0.7846082326461877 |

## 阶段与淘汰原因


### seed-0 · Validation

- **父方案**: —
- **结果代码**: Accepted [Accepted]

```json
{
  "work": {
    "functionEvaluations": 1,
    "gradientEvaluations": 0,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 44520,
    "geometryProbes": 2131,
    "elapsed": "00:00:00.2682086"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Recovery

- **父方案**: seed-0
- **结果代码**: BudgetExhausted [BudgetExhausted]
- **原因**: Direct recovery failed; beginning original-relative gradual deletion.

```json
{
  "work": {
    "functionEvaluations": 400,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 352,
    "differenceEvaluations": 364,
    "jacobianBuilds": 13,
    "tracedRays": 0,
    "geometryProbes": 744108,
    "elapsed": "00:00:00.9901696"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Recovery

- **父方案**: seed-0
- **结果代码**: Recovered [Recovered]

```json
{
  "work": {
    "functionEvaluations": 89,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 85,
    "differenceEvaluations": 84,
    "jacobianBuilds": 3,
    "tracedRays": 0,
    "geometryProbes": 187260,
    "elapsed": "00:00:00.3021004"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Feasibility

- **父方案**: seed-0
- **结果代码**: UnmetTargets [UnmetTargets]

```json
{
  "work": {
    "functionEvaluations": 1,
    "gradientEvaluations": 0,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 44520,
    "geometryProbes": 2131,
    "elapsed": "00:00:00.2283853"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Local

- **父方案**: seed-0
- **结果代码**: UpdateLimit [UpdateLimit]

```json
{
  "work": {
    "functionEvaluations": 9,
    "gradientEvaluations": 9,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 15822,
    "geometryProbes": 19179,
    "elapsed": "00:00:07.3264231"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Feasibility

- **父方案**: seed-0
- **结果代码**: UnmetTargets [UnmetTargets]

```json
{
  "work": {
    "functionEvaluations": 1,
    "gradientEvaluations": 0,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 44520,
    "geometryProbes": 2131,
    "elapsed": "00:00:00.2468337"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Full

- **父方案**: seed-0
- **结果代码**: UpdateLimit [UpdateLimit]

```json
{
  "work": {
    "functionEvaluations": 13,
    "gradientEvaluations": 13,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 22854,
    "geometryProbes": 27703,
    "elapsed": "00:00:11.9812110"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · TransitionValidation

- **父方案**: seed-0
- **结果代码**: TransitionRejected [TransitionRejected]

```json
{
  "work": {
    "functionEvaluations": 1,
    "gradientEvaluations": 0,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 44520,
    "geometryProbes": 2131,
    "elapsed": "00:00:00.2707167"
  },
  "knownFunctionEvaluations": null
}
```


### construction:1 · Construction

- **父方案**: —
- **结果代码**: OriginalAssessed [OriginalAssessed]

```json
{
  "work": {
    "functionEvaluations": 15,
    "gradientEvaluations": 0,
    "validationEvaluations": 1,
    "matrixEvaluations": 36,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 44520,
    "geometryProbes": 27703,
    "elapsed": "00:00:01.7981007"
  },
  "knownFunctionEvaluations": null
}
```


### construction:2 · Construction

- **父方案**: —
- **结果代码**: OriginalAssessed [OriginalAssessed]

```json
{
  "work": {
    "functionEvaluations": 15,
    "gradientEvaluations": 0,
    "validationEvaluations": 1,
    "matrixEvaluations": 36,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 44520,
    "geometryProbes": 27703,
    "elapsed": "00:00:01.1104192"
  },
  "knownFunctionEvaluations": null
}
```


### construction:3 · Construction

- **父方案**: —
- **结果代码**: OriginalAssessed [OriginalAssessed]

```json
{
  "work": {
    "functionEvaluations": 15,
    "gradientEvaluations": 0,
    "validationEvaluations": 1,
    "matrixEvaluations": 42,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 44520,
    "geometryProbes": 31965,
    "elapsed": "00:00:01.1293894"
  },
  "knownFunctionEvaluations": null
}
```


### construction:4 · Construction

- **父方案**: —
- **结果代码**: Rejected:This thick layout has no geometrically feasible realization within its proposal budget. [Rejected:This thick layout has no geometrically feasible realization within its proposal budget.]

```json
{
  "work": {
    "functionEvaluations": 13,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 18,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 0,
    "geometryProbes": 12786,
    "elapsed": "00:00:00.0296900"
  },
  "knownFunctionEvaluations": null
}
```


### construction:5 · Construction

- **父方案**: —
- **结果代码**: Rejected:No physical space for this descriptor around the fixed stop. [Rejected:No physical space for this descriptor around the fixed stop.]

```json
{
  "work": {
    "functionEvaluations": 0,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 0,
    "geometryProbes": 0,
    "elapsed": "00:00:00.0015443"
  },
  "knownFunctionEvaluations": null
}
```


### construction:6 · Construction

- **父方案**: —
- **结果代码**: Rejected:No physical space for this descriptor around the fixed stop. [Rejected:No physical space for this descriptor around the fixed stop.]

```json
{
  "work": {
    "functionEvaluations": 0,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 0,
    "geometryProbes": 0,
    "elapsed": "00:00:00.0015488"
  },
  "knownFunctionEvaluations": null
}
```


### construction:7 · Construction

- **父方案**: —
- **结果代码**: Rejected:No physical space for this descriptor around the fixed stop. [Rejected:No physical space for this descriptor around the fixed stop.]

```json
{
  "work": {
    "functionEvaluations": 0,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 0,
    "geometryProbes": 0,
    "elapsed": "00:00:00.0016604"
  },
  "knownFunctionEvaluations": null
}
```


### construction:8 · Construction

- **父方案**: —
- **结果代码**: Rejected:No physical space for this descriptor around the fixed stop. [Rejected:No physical space for this descriptor around the fixed stop.]

```json
{
  "work": {
    "functionEvaluations": 0,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 0,
    "geometryProbes": 0,
    "elapsed": "00:00:00.0016801"
  },
  "knownFunctionEvaluations": null
}
```


### construction:9 · Construction

- **父方案**: —
- **结果代码**: Rejected:No physical space for this descriptor around the fixed stop. [Rejected:No physical space for this descriptor around the fixed stop.]

```json
{
  "work": {
    "functionEvaluations": 0,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 0,
    "geometryProbes": 0,
    "elapsed": "00:00:00.0024079"
  },
  "knownFunctionEvaluations": null
}
```


### compensation:0 · SphereSparse

- **父方案**: —
- **结果代码**: BudgetExhausted [BudgetExhausted]
- **原因**: Gate attempt 1: before=(0, 98.75743682194769, 0.8376700910881646), after=(0, 28.681132249104287, 5.017611112215532), rank=32, rate=0.01, accepted=True.; Gate attempt 2: before=(0, 28.681132249104287, 5.017611112215532), after=(0, 9.0401033470048, 5.428740677167934), rank=32, rate=0.01, accepted=True.; Gate attempt 3: before=(0, 9.0401033470048, 5.428740677167934), after=(0, 7.168151467334055, 5.980164706147932), rank=32, rate=0.01, accepted=True.; Gate attempt 4: before=(0, 7.168151467334055, 5.980164706147932), after=(0, 6.6603989199088165, 5.70582141345552), rank=32, rate=0.01, accepted=True.; Gate attempt 5: before=(0, 6.6603989199088165, 5.70582141345552), after=(0, 6.261522569914779, 5.413601867714172), rank=32, rate=0.01, accepted=True.; Gate attempt 6: before=(0, 6.261522569914779, 5.413601867714172), after=(0, 5.899910491126799, 5.14043000166661), rank=32, rate=0.01, accepted=True.; Gate attempt 7: before=(0, 5.899910491126799, 5.14043000166661), after=(0, 5.566799403609355, 4.886183815553956), rank=32, rate=0.01, accepted=True.; Gate attempt 8: before=(0, 5.566799403609355, 4.886183815553956), after=(0, 5.258226746088923, 4.648017206184292), rank=32, rate=0.01, accepted=True.; Gate attempt 9: before=(0, 5.258226746088923, 4.648017206184292), after=(0, 4.971176648187641, 4.423628887003433), rank=32, rate=0.01, accepted=True.; Gate attempt 10: before=(0, 4.971176648187641, 4.423628887003433), after=(0, 4.7032142199367755, 4.211261409252072), rank=32, rate=0.01, accepted=True.; Gate attempt 11: before=(0, 4.7032142199367755, 4.211261409252072), after=(0, 4.4523519557468205, 4.009575413629666), rank=32, rate=0.01, accepted=True.; Gate attempt 12: before=(0, 4.4523519557468205, 4.009575413629666), after=(0, 4.216956877132667, 3.817550138926378), rank=32, rate=0.01, accepted=True.; Gate attempt 13: before=(0, 4.216956877132667, 3.817550138926378), after=(0, 3.9956794482985467, 3.6344078824386328), rank=32, rate=0.01, accepted=True.; Gate attempt 14: before=(0, 3.9956794482985467, 3.6344078824386328), after=(0, 3.787397702005203, 3.459555418524757), rank=32, rate=0.01, accepted=True.; Gate attempt 15: before=(0, 3.787397702005203, 3.459555418524757), after=(0, 3.5911724921305836, 3.3561251644932795), rank=32, rate=0.01, accepted=True.; Gate attempt 16: before=(0, 3.5911724921305836, 3.3561251644932795), after=(0, 3.42504125780673, 3.253930686226091), rank=32, rate=0.01, accepted=True.; Gate attempt 17: before=(0, 3.42504125780673, 3.253930686226091), after=(0, 3.301428512896107, 3.1279632586747397), rank=32, rate=0.01, accepted=True.; Gate attempt 18: before=(0, 3.301428512896107, 3.1279632586747397), after=(0, 3.1865274981674125, 3.0050341019836186), rank=32, rate=0.01, accepted=True.; Gate attempt 19: before=(0, 3.1865274981674125, 3.0050341019836186), after=(0, 3.077733767803992, 2.8868459461819596), rank=32, rate=0.01, accepted=True.; Gate attempt 20: before=(0, 3.077733767803992, 2.8868459461819596), after=(0, 2.9744423109139486, 2.773170197777038), rank=32, rate=0.01, accepted=True.; Gate attempt 21: before=(0, 2.9744423109139486, 2.773170197777038), after=(0, 2.876203276073744, 2.6636733331619036), rank=32, rate=0.01, accepted=True.; Gate attempt 22: before=(0, 2.876203276073744, 2.6636733331619036), after=(0, 2.759997169540043, 2.526969780072338), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 23: original=(3.444135005563101E-08, 2.6522435004490905, 2.3905667553708834).; Gate attempt 23: before=(0, 2.759997169540043, 2.526969780072338), after=(0, 2.6522286875628125, 2.391016669973724), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 24: original=(7.184663860813847E-10, 2.621531526729915, 2.2500740410371103).; Gate attempt 24: before=(0, 2.6522286875628125, 2.391016669973724), after=(0, 2.621529400516868, 2.2501367336154687), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 25: original=(6.907768783409204E-10, 2.5492632714791195, 2.162398521506416).; Gate attempt 25: before=(0, 2.621529400516868, 2.2501367336154687), after=(0, 2.549261224302969, 2.1624586802903942), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 26: original=(6.377850579391064E-10, 2.476937373699866, 2.0798230988566493).; Gate attempt 26: before=(0, 2.549261224302969, 2.1624586802903942), after=(0, 2.4769354397497643, 2.0798796930837646), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 27: original=(5.887711032992446E-10, 2.3984619872613124, 2.0070856892581896).; Gate attempt 27: before=(0, 2.4769354397497643, 2.0798796930837646), after=(0, 2.398460162379899, 2.007139046334455), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 28: original=(5.30439951312058E-10, 2.3241500964207513, 1.9340611110105548).; Gate attempt 28: before=(0, 2.398460162379899, 2.007139046334455), after=(0, 2.3241483945224126, 1.9341107493015146), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 29: original=(4.735333827480602E-10, 2.2516090972423437, 1.8628811274719839).; Gate attempt 29: before=(0, 2.3241483945224126, 1.9341107493015146), after=(0, 2.2516075175366277, 1.862927076064992), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 30: original=(4.178137918512234E-10, 2.180626819397892, 1.7935082064973127).; Gate attempt 30: before=(0, 2.2516075175366277, 1.862927076064992), after=(0, 2.180625361994304, 1.793550473876974), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 31: original=(3.6415312999050476E-10, 2.111118946574756, 1.7258071299815412).; Gate attempt 31: before=(0, 2.180625361994304, 1.793550473876974), after=(0, 2.111117610584238, 1.725845755488737), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 32: original=(3.1334438823852523E-10, 2.0430202160000284, 1.6596531679969484).; Gate attempt 32: before=(0, 2.111117610584238, 1.725845755488737), after=(0, 2.043018999495239, 1.6596882224849299), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 33: original=(2.6603123028977854E-10, 1.97627797491522, 1.5949372395964485).; Gate attempt 33: before=(0, 2.043018999495239, 1.6596882224849299), after=(0, 1.9762768749935111, 1.5949688232464458), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 34: original=(2.2269894844937456E-10, 1.910851186957844, 1.531565789742363).; Gate attempt 34: before=(0, 1.9762768749935111, 1.5949688232464458), after=(0, 1.910850199815266, 1.5315940295279353), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 35: original=(1.8367328504493496E-10, 1.8467098042797447, 1.4694604619603466).; Gate attempt 35: before=(0, 1.910850199815266, 1.5315940295279353), after=(0, 1.8467089252923183, 1.4694855088764887), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 36: original=(1.4912429426608964E-10, 1.7838342284765094, 1.4085578002778665).; Gate attempt 36: before=(0, 1.8467089252923183, 1.4694855088764887), after=(0, 1.783833452290095, 1.4085798263120697), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 37: original=(1.1907535960241693E-10, 1.7221369444418457, 1.3488089643512813).; Gate attempt 37: before=(0, 1.783833452290095, 1.4085798263120697), after=(0, 1.7221362629170665, 1.3488281591989035), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 38: original=(9.542721532877067E-11, 1.6539763020503702, 1.296925802418048).; Gate attempt 38: before=(0, 1.7221362629170665, 1.3488281591989035), after=(0, 1.6539757040109777, 1.2969426126390002), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 39: original=(7.271754471344657E-11, 1.5896608156312613, 1.243096774471467).; Gate attempt 39: before=(0, 1.6539757040109777, 1.2969426126390002), after=(0, 1.5896603036699974, 1.243111097460958), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 40: original=(5.45828690480245E-11, 1.5265707498242747, 1.1899609778499292).; Gate attempt 40: before=(0, 1.5896603036699974, 1.243111097460958), after=(0, 1.5265703150745753, 1.1899730792291094), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 41: original=(4.001269238122488E-11, 1.4645799730274802, 1.1376514256277184).; Gate attempt 41: before=(0, 1.5265703150745753, 1.1899730792291094), after=(0, 1.4645796078405235, 1.1376615211707102), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 42: original=(2.929764438877721E-11, 1.3975913151852775, 1.0914333994664538).; Gate attempt 42: before=(0, 1.4645796078405235, 1.1376615211707102), after=(0, 1.3975910084891126, 1.0914418402395298), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 43: original=(2.012790141474355E-11, 1.3325075543181044, 1.0447928791131973).; Gate attempt 43: before=(0, 1.3975910084891126, 1.0914418402395298), after=(0, 1.3325073048689646, 1.044799705162053), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 44: original=(1.3403641439599054E-11, 1.2684304139192881, 0.9987261796163776).; Gate attempt 44: before=(0, 1.3325073048689646, 1.044799705162053), after=(0, 1.2684302142872823, 0.99873160979914), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 45: original=(8.579228320436951E-12, 1.2053699071841075, 0.9533164996010017).; Gate attempt 45: before=(0, 1.2684302142872823, 0.99873160979914), after=(0, 1.205369750648563, 0.9533207312082297), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 46: original=(5.249075784709511E-12, 1.143393559769916, 0.9086104756706482).; Gate attempt 46: before=(0, 1.205369750648563, 0.9533207312082297), after=(0, 1.1433934398359624, 0.9086136971908407), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 47: original=(3.0506802706846265E-12, 1.0825802482830869, 0.8646611909756328).; Gate attempt 47: before=(0, 1.1433934398359624, 0.9086136971908407), after=(0, 1.0825801587707304, 0.8646635796280724), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 48: original=(1.6717807923618294E-12, 1.0230167602337552, 0.8215295810377944).; Gate attempt 48: before=(0, 1.0825801587707304, 0.8646635796280724), after=(0, 1.023016695385176, 0.8215312999527701), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 49: original=(8.566367793807347E-13, 0.9647965227977922, 0.7792832647625535).; Gate attempt 49: before=(0, 1.023016695385176, 0.8215312999527701), after=(0, 0.9647964773699993, 0.7792844606779253), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 50: original=(4.0703090506431495E-13, 0.9080181967802013, 0.7379950162248906).; Gate attempt 50: before=(0, 0.9647964773699993, 0.7792844606779253), after=(0, 0.9080181661171958, 0.7379958178450404), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 51: original=(1.7848573841444783E-13, 0.8527840059794469, 0.6977168315879695).; Gate attempt 51: before=(0, 0.9080181661171958, 0.7379958178450404), after=(0, 0.8527839860605234, 0.697717349961014), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 52: original=(5.917552060187621E-14, 0.796120072307914, 0.6508749227544499).; Gate attempt 52: before=(0, 0.8527839860605234, 0.697717349961014), after=(0, 0.7961200609825562, 0.650875214651214), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 53: original=(2.1815582133694104E-14, 0.7400977916367203, 0.6061254789109732).; Gate attempt 53: before=(0, 0.7961200609825562, 0.650875214651214), after=(0, 0.7400977848066722, 0.6061256535025287), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 54: original=(1.600058176842259E-14, 0.6861036188998706, 0.563008896135632).; Gate attempt 54: before=(0, 0.7400977848066722, 0.6061256535025287), after=(0, 0.6861036131848204, 0.5630090410434401), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 55: original=(2.879965280061562E-14, 0.634350032190104, 0.5215570008525963).; Gate attempt 55: before=(0, 0.6861036131848204, 0.5630090410434401), after=(0, 0.6343500249292722, 0.5215571834618599), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 56: original=(6.695197073087856E-14, 0.5849544707882088, 0.4818787456375356).; Gate attempt 56: before=(0, 0.6343500249292722, 0.5215571834618599), after=(0, 0.5849544603301066, 0.4818790065287499), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 57: original=(1.4095331159600285E-13, 0.538018685233743, 0.44407551887986685).; Gate attempt 57: before=(0, 0.5849544603301066, 0.4818790065287499), after=(0, 0.5380186708175477, 0.44407587561665285), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 58: original=(2.532981671366739E-13, 0.4936293568125954, 0.40823356453696336).; Gate attempt 58: before=(0, 0.5380186708175477, 0.44407587561665285), after=(0, 0.4936293383992158, 0.4082340165554099), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 59: original=(3.946612327940222E-13, 0.4518549636158568, 0.3765307073537099).; Gate attempt 59: before=(0, 0.4936293383992158, 0.4082340165554099), after=(0, 0.4518549417044614, 0.3765312493951079), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 60: original=(1.8502566076925938E-13, 0.4100865230704457, 0.3549735041390982).; Gate attempt 60: before=(0, 0.4518549417044614, 0.3765312493951079), after=(0, 0.4100865087180972, 0.3549738556257897), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 61: original=(1.0594286174233833E-12, 0.37637068915888755, 0.3205209686892141).; Gate attempt 61: before=(0, 0.4100865087180972, 0.3549738556257897), after=(0, 0.3763706567428959, 0.32052174976408704), rank=32, rate=0.01, accepted=True.

```json
{
  "work": {
    "functionEvaluations": 102,
    "gradientEvaluations": 101,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 134814,
    "geometryProbes": 217362,
    "elapsed": "00:00:24.6439079"
  },
  "knownFunctionEvaluations": null
}
```


### compensation:1 · Even4Sparse

- **父方案**: —
- **结果代码**: Converged [Converged]
- **原因**: Hard-constraint correction attempt 1: original=(3.656006282263109E-11, 0.06630147078083315, 0.03289980666285377).; Gate attempt 1: before=(0, 0.37637065689904164, 0.32052174957976426), after=(0, 0.06630140926326683, 0.03290099566551925), rank=48, rate=0.01, accepted=True.; Hard-constraint correction attempt 2: original=(1.2668681767915306E-14, 0.020636078623133176, 0.007847261876207696).; Gate attempt 2: before=(0, 0.06630140926326683, 0.03290099566551925), after=(0, 0.02063607814824876, 0.00784727285795981), rank=48, rate=0.01, accepted=True.; Hard-constraint correction attempt 3: original=(4.3685707310032595E-13, 0.001991323351444392, 0.003722167986223438).; Gate attempt 3: before=(0, 0.02063607814824876, 0.00784727285795981), after=(0, 0.0019913227432513153, 0.0037221938107787926), rank=48, rate=0.01, accepted=True.; Gate attempt 4: before=(0, 0.0019913227432513153, 0.0037221938107787926), after=(0, 0.0007306285426977572, 0.000537431412688008), rank=48, rate=0.01, accepted=True.; Hard-constraint correction attempt 5: original=(1.584134120511193E-16, 0.0002814337609075354, 0.00012587335674052303).; Gate attempt 5: before=(0, 0.0007306285426977572, 0.000537431412688008), after=(0, 0.0002812903766472603, 0.00012709140476114172), rank=48, rate=0.01, accepted=True.; Hard-constraint correction attempt 6: original=(1.3897133435333413E-16, 0.00012489445461555612, 1.7461481063828652E-05).; Gate attempt 6: before=(0, 0.0002812903766472603, 0.00012709140476114172), after=(0, 0.00012489444982286406, 1.7461534315008326E-05), rank=48, rate=0.01, accepted=True.; Gate attempt 7: before=(0, 0.00012489444982286406, 1.7461534315008326E-05), after=(0, 2.031504119451069E-05, 1.986074676132509E-06), rank=48, rate=0.01, accepted=True.; Gate attempt 8: before=(0, 2.031504119451069E-05, 1.986074676132509E-06), after=(0, 3.1183153235819375E-06, 2.831350625842945E-07), rank=48, rate=0.01, accepted=True.; Gate attempt 9: before=(0, 3.1183153235819375E-06, 2.831350625842945E-07), after=(0, 4.724417738970989E-07, 4.2099003929660915E-08), rank=48, rate=0.01, accepted=True.; Gate attempt 10: before=(0, 4.724417738970989E-07, 4.2099003929660915E-08), after=(0, 7.129116474626917E-08, 6.313581730254482E-09), rank=48, rate=0.01, accepted=True.; Gate attempt 11: before=(0, 7.129116474626917E-08, 6.313581730254482E-09), after=(0, 1.0742374131794862E-08, 9.491873289319777E-10), rank=48, rate=0.01, accepted=True.; Gate attempt 12: before=(0, 1.0742374131794862E-08, 9.491873289319777E-10), after=(0, 4.1833532852629455E-28, 1.4282482794248913E-10), rank=48, rate=0.01, accepted=True.

```json
{
  "work": {
    "functionEvaluations": 19,
    "gradientEvaluations": 18,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 60612,
    "geometryProbes": 40489,
    "elapsed": "00:00:06.3229031"
  },
  "knownFunctionEvaluations": null
}
```


### compensation:2 · Even6

- **父方案**: —
- **结果代码**: Converged [Converged]
- **原因**: Gate attempt 1: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 8.721873819442916E-05, 1.589197203285184E-08), rank=64, rate=0.01, accepted=False.; Gate attempt 2: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 8.721873819442916E-05, 1.589197203285184E-08), rank=64, rate=0.005, accepted=False.; Gate attempt 3: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 8.721873819442916E-05, 1.589197203285184E-08), rank=64, rate=0.0025, accepted=False.; Gate attempt 4: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 8.37261938492282E-05, 1.8676136420333663E-08), rank=64, rate=0.00125, accepted=False.; Gate attempt 5: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 5.063415957596448E-05, 3.6409330916804273E-08), rank=64, rate=0.000625, accepted=False.; Gate attempt 6: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 1.9516204794473335E-05, 6.376724706592001E-08), rank=64, rate=0.0003125, accepted=False.; Gate attempt 7: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 3.7794310923098115E-06, 9.327859349769029E-08), rank=64, rate=0.00015625, accepted=True.; Gate attempt 8: before=(0, 3.7794310923098115E-06, 9.327859349769029E-08), after=(0, 4.1833532852629455E-28, 1.5936277393089257E-10), rank=64, rate=0.0001875, accepted=True.

```json
{
  "work": {
    "functionEvaluations": 10,
    "gradientEvaluations": 9,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 101814,
    "geometryProbes": 21310,
    "elapsed": "00:00:23.2150111"
  },
  "knownFunctionEvaluations": null
}
```


### preparation:status · Finished

- **父方案**: —
- **结果代码**: Completed [Completed]
- **原因**: kernel=9cb73aa45d909214003215baf29f4738feb08d39b2a330052c5439b8a8aa4671

```json
{
  "work": {
    "functionEvaluations": 189,
    "gradientEvaluations": 128,
    "validationEvaluations": 6,
    "matrixEvaluations": 132,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 430800,
    "geometryProbes": 379318,
    "elapsed": "00:00:58.2850304"
  },
  "knownFunctionEvaluations": null
}
```

