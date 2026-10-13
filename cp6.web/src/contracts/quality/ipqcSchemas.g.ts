// Generated; native roots preserve registry predicates. R4 roots adapt exact composed TypeScript declarations.
import type * as Native from "./ipqc.g"
import type { Schema } from "./schema"

export interface IpqcContracts {
  EnvironmentKey: Native.EnvironmentKey
  QapRef: Native.QapRef
  QapEvidence: Native.QapEvidence
  QapUom: Native.QapUom
  QapRange: Native.QapRange
  ExecRef: Native.ExecRef
  ExecEvidence: Native.ExecEvidence
  ExecRange: Native.ExecRange
  ExecScope: Native.ExecScope
  ExecQuantity: Native.ExecQuantity
  ExecQaSource: Native.ExecQaSource
  OutRef: Native.OutRef
  OutEvidence: Native.OutEvidence
  OutFlatRef: Native.OutFlatRef
  OutRange: Native.OutRange
  OutQualitySource: Native.OutQualitySource
  OutQualityTaskResult: Native.OutQualityTaskResult
  OutQualityDisposition: Native.OutQualityDisposition
  ExecQualityTaskResult: Native.ExecQualityTaskResult
  ExecQualityDisposition: Native.ExecQualityDisposition
  RawEvidenceHandoff: Native.RawEvidenceHandoff
  R4FrozenPayload: Native.R4FrozenPayload
  R4QapFullCountPlanContent: Native.R4QapFullCountPlanContent
}

export const ipqcSchemas: Readonly<Record<keyof IpqcContracts, Schema>> = {
  "EnvironmentKey": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "tenantId": {
        "type": "string",
        "format": "uuid"
      },
      "environmentId": {
        "type": "string",
        "format": "uuid"
      },
      "mode": {
        "type": "string",
        "enum": [
          "REAL",
          "DEMO"
        ]
      }
    },
    "required": [
      "tenantId",
      "environmentId",
      "mode"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:EnvironmentKey"
  },
  "QapRef": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "owner": {
        "type": "string",
        "minLength": 1,
        "maxLength": 256
      },
      "id": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "version": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      }
    },
    "required": [
      "owner",
      "id",
      "version"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapRef"
  },
  "QapEvidence": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "owner": {
        "type": "string",
        "minLength": 1,
        "maxLength": 256
      },
      "id": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "version": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      },
      "digest": {
        "type": "string",
        "pattern": "^[0-9a-f]{64}$"
      }
    },
    "required": [
      "owner",
      "id",
      "version",
      "digest"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapEvidence"
  },
  "QapUom": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "id": {
        "type": "string",
        "minLength": 1,
        "maxLength": 256
      },
      "version": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      }
    },
    "required": [
      "id",
      "version"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapUom"
  },
  "QapRange": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "from": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
      },
      "to": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
      }
    },
    "required": [
      "from",
      "to"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapRange"
  },
  "ExecRef": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "owner": {
        "type": "string",
        "minLength": 1,
        "maxLength": 256
      },
      "id": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "version": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      }
    },
    "required": [
      "owner",
      "id",
      "version"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:ExecRef"
  },
  "ExecEvidence": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "ref": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          }
        },
        "required": [
          "owner",
          "id",
          "version"
        ]
      },
      "digest": {
        "type": "string",
        "pattern": "^[0-9a-f]{64}$"
      }
    },
    "required": [
      "ref",
      "digest"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:ExecEvidence"
  },
  "ExecRange": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "portionId": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "from": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
      },
      "to": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
      }
    },
    "required": [
      "portionId",
      "from",
      "to"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:ExecRange"
  },
  "ExecScope": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "scopeId": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "scopeVersion": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      },
      "ranges": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "portionId": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "portionId",
            "from",
            "to"
          ]
        },
        "minItems": 1
      }
    },
    "required": [
      "scopeId",
      "scopeVersion",
      "ranges"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:ExecScope"
  },
  "ExecQuantity": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "value": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
      },
      "unitRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          }
        },
        "required": [
          "owner",
          "id",
          "version"
        ]
      }
    },
    "required": [
      "value",
      "unitRef"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:ExecQuantity"
  },
  "ExecQaSource": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "schema": {
        "const": "EXEC-QA-SOURCE-1"
      },
      "sourceId": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "revision": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      },
      "previousRevision": {
        "oneOf": [
          {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          {
            "type": "null"
          }
        ]
      },
      "workOrderId": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "operationId": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "actualManifestRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "reportRefs": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "ref": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "owner": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 256
                },
                "id": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 160
                },
                "version": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]*)$"
                }
              },
              "required": [
                "owner",
                "id",
                "version"
              ]
            },
            "digest": {
              "type": "string",
              "pattern": "^[0-9a-f]{64}$"
            }
          },
          "required": [
            "ref",
            "digest"
          ]
        },
        "minItems": 0
      },
      "scope": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "scopeId": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "scopeVersion": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "ranges": {
            "type": "array",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "portionId": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 160
                },
                "from": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
                },
                "to": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
                }
              },
              "required": [
                "portionId",
                "from",
                "to"
              ]
            },
            "minItems": 1
          }
        },
        "required": [
          "scopeId",
          "scopeVersion",
          "ranges"
        ]
      },
      "inspectionPolicyRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          }
        },
        "required": [
          "owner",
          "id",
          "version"
        ]
      },
      "actualBaselineRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          }
        },
        "required": [
          "owner",
          "id",
          "version"
        ]
      },
      "quantity": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "value": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
          },
          "unitRef": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          }
        },
        "required": [
          "value",
          "unitRef"
        ]
      },
      "evidenceRefs": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "ref": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "owner": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 256
                },
                "id": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 160
                },
                "version": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]*)$"
                }
              },
              "required": [
                "owner",
                "id",
                "version"
              ]
            },
            "digest": {
              "type": "string",
              "pattern": "^[0-9a-f]{64}$"
            }
          },
          "required": [
            "ref",
            "digest"
          ]
        },
        "minItems": 1
      },
      "digest": {
        "type": "string",
        "pattern": "^[0-9a-f]{64}$"
      }
    },
    "required": [
      "schema",
      "sourceId",
      "revision",
      "previousRevision",
      "workOrderId",
      "operationId",
      "actualManifestRef",
      "reportRefs",
      "scope",
      "inspectionPolicyRef",
      "actualBaselineRef",
      "quantity",
      "evidenceRefs",
      "digest"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:ExecQaSource"
  },
  "OutRef": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "owner": {
        "type": "string",
        "minLength": 1,
        "maxLength": 256
      },
      "id": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "version": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      }
    },
    "required": [
      "owner",
      "id",
      "version"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:OutRef"
  },
  "OutEvidence": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "ref": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          }
        },
        "required": [
          "owner",
          "id",
          "version"
        ]
      },
      "digest": {
        "type": "string",
        "pattern": "^[0-9a-f]{64}$"
      }
    },
    "required": [
      "ref",
      "digest"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:OutEvidence"
  },
  "OutFlatRef": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "owner": {
        "type": "string",
        "minLength": 1,
        "maxLength": 256
      },
      "id": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "version": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      },
      "digest": {
        "type": "string",
        "pattern": "^[0-9a-f]{64}$"
      }
    },
    "required": [
      "owner",
      "id",
      "version",
      "digest"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:OutFlatRef"
  },
  "OutRange": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "from": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
      },
      "to": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
      }
    },
    "required": [
      "from",
      "to"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:OutRange"
  },
  "OutQualitySource": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "schema": {
        "const": "OUT-QA-SOURCE-1"
      },
      "sourceKey": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "revision": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      },
      "previousRef": {
        "oneOf": [
          {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "ref": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "owner": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 256
                  },
                  "id": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 160
                  },
                  "version": {
                    "type": "string",
                    "pattern": "^(0|[1-9][0-9]*)$"
                  }
                },
                "required": [
                  "owner",
                  "id",
                  "version"
                ]
              },
              "digest": {
                "type": "string",
                "pattern": "^[0-9a-f]{64}$"
              }
            },
            "required": [
              "ref",
              "digest"
            ]
          },
          {
            "type": "null"
          }
        ]
      },
      "outputRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "outputRanges": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "from",
            "to"
          ]
        },
        "minItems": 1
      },
      "outputUnitRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          }
        },
        "required": [
          "owner",
          "id",
          "version"
        ]
      },
      "actualBaselineRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          }
        },
        "required": [
          "owner",
          "id",
          "version"
        ]
      },
      "purposeRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          }
        },
        "required": [
          "owner",
          "id",
          "version"
        ]
      },
      "policyRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "owner",
          "id",
          "version",
          "digest"
        ]
      },
      "stage": {
        "type": "string",
        "enum": [
          "FQC",
          "TRIAL_INSPECTION"
        ]
      },
      "receiptMappings": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "receiptRef": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "owner": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 256
                },
                "id": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 160
                },
                "version": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]*)$"
                }
              },
              "required": [
                "owner",
                "id",
                "version"
              ]
            },
            "lineId": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "outputRange": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "from": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
                },
                "to": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
                }
              },
              "required": [
                "from",
                "to"
              ]
            },
            "rootRange": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "from": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
                },
                "to": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
                }
              },
              "required": [
                "from",
                "to"
              ]
            },
            "conversionRef": {
              "oneOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "owner": {
                      "type": "string",
                      "minLength": 1,
                      "maxLength": 256
                    },
                    "id": {
                      "type": "string",
                      "minLength": 1,
                      "maxLength": 160
                    },
                    "version": {
                      "type": "string",
                      "pattern": "^(0|[1-9][0-9]*)$"
                    },
                    "digest": {
                      "type": "string",
                      "pattern": "^[0-9a-f]{64}$"
                    }
                  },
                  "required": [
                    "owner",
                    "id",
                    "version",
                    "digest"
                  ]
                },
                {
                  "type": "null"
                }
              ]
            },
            "receiptSubject": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "receiptSource": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "producer": {
                      "type": "string",
                      "minLength": 1,
                      "maxLength": 256
                    },
                    "sourceKind": {
                      "type": "string",
                      "minLength": 1,
                      "maxLength": 256
                    },
                    "documentId": {
                      "type": "string",
                      "minLength": 1,
                      "maxLength": 256
                    },
                    "lineId": {
                      "type": "string",
                      "minLength": 1,
                      "maxLength": 256
                    },
                    "executionKey": {
                      "type": "string",
                      "minLength": 1,
                      "maxLength": 256
                    },
                    "sourceVersion": {
                      "type": "string",
                      "pattern": "^(0|[1-9][0-9]*)$"
                    }
                  },
                  "required": [
                    "producer",
                    "sourceKind",
                    "documentId",
                    "lineId",
                    "executionKey",
                    "sourceVersion"
                  ]
                },
                "receiptLineKey": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 256
                },
                "range": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "from": {
                      "type": "string",
                      "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
                    },
                    "to": {
                      "type": "string",
                      "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
                    }
                  },
                  "required": [
                    "from",
                    "to"
                  ]
                }
              },
              "required": [
                "receiptSource",
                "receiptLineKey",
                "range"
              ]
            }
          },
          "required": [
            "receiptRef",
            "lineId",
            "receiptSubject",
            "outputRange",
            "rootRange",
            "conversionRef"
          ]
        },
        "minItems": 0
      },
      "sampleBatchRef": {
        "oneOf": [
          {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              },
              "digest": {
                "type": "string",
                "pattern": "^[0-9a-f]{64}$"
              }
            },
            "required": [
              "owner",
              "id",
              "version",
              "digest"
            ]
          },
          {
            "type": "null"
          }
        ]
      },
      "sourceCoverageRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      }
    },
    "required": [
      "schema",
      "sourceKey",
      "revision",
      "previousRef",
      "outputRef",
      "outputRanges",
      "outputUnitRef",
      "actualBaselineRef",
      "purposeRef",
      "policyRef",
      "stage",
      "receiptMappings",
      "sampleBatchRef",
      "sourceCoverageRef"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:OutQualitySource"
  },
  "OutQualityTaskResult": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "reference": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "sourceKey": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "sourceRevision": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      },
      "policyRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "owner",
          "id",
          "version",
          "digest"
        ]
      },
      "state": {
        "type": "string",
        "enum": [
          "CREATED",
          "EXEMPT_RECORDED",
          "WAITING",
          "REJECTED"
        ]
      },
      "taskRef": {
        "oneOf": [
          {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "ref": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "owner": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 256
                  },
                  "id": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 160
                  },
                  "version": {
                    "type": "string",
                    "pattern": "^(0|[1-9][0-9]*)$"
                  }
                },
                "required": [
                  "owner",
                  "id",
                  "version"
                ]
              },
              "digest": {
                "type": "string",
                "pattern": "^[0-9a-f]{64}$"
              }
            },
            "required": [
              "ref",
              "digest"
            ]
          },
          {
            "type": "null"
          }
        ]
      },
      "exemptionRef": {
        "oneOf": [
          {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "ref": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "owner": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 256
                  },
                  "id": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 160
                  },
                  "version": {
                    "type": "string",
                    "pattern": "^(0|[1-9][0-9]*)$"
                  }
                },
                "required": [
                  "owner",
                  "id",
                  "version"
                ]
              },
              "digest": {
                "type": "string",
                "pattern": "^[0-9a-f]{64}$"
              }
            },
            "required": [
              "ref",
              "digest"
            ]
          },
          {
            "type": "null"
          }
        ]
      },
      "canonicalSourceRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "coveredOutputRanges": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "from",
            "to"
          ]
        },
        "minItems": 0
      },
      "coverageRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "reasonCodes": {
        "type": "array",
        "items": {
          "type": "string",
          "minLength": 1,
          "maxLength": 256
        },
        "minItems": 0
      }
    },
    "required": [
      "reference",
      "sourceKey",
      "sourceRevision",
      "policyRef",
      "state",
      "taskRef",
      "exemptionRef",
      "canonicalSourceRef",
      "coveredOutputRanges",
      "coverageRef",
      "reasonCodes"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:OutQualityTaskResult"
  },
  "OutQualityDisposition": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "schema": {
        "const": "OUT-QA-DISPOSITION-1"
      },
      "reference": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "sourceRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "taskRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "revision": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      },
      "previousRef": {
        "oneOf": [
          {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "ref": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "owner": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 256
                  },
                  "id": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 160
                  },
                  "version": {
                    "type": "string",
                    "pattern": "^(0|[1-9][0-9]*)$"
                  }
                },
                "required": [
                  "owner",
                  "id",
                  "version"
                ]
              },
              "digest": {
                "type": "string",
                "pattern": "^[0-9a-f]{64}$"
              }
            },
            "required": [
              "ref",
              "digest"
            ]
          },
          {
            "type": "null"
          }
        ]
      },
      "outputRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "policyRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "owner",
          "id",
          "version",
          "digest"
        ]
      },
      "authorityRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "accepted": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "from",
            "to"
          ]
        },
        "minItems": 0
      },
      "rejected": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "from",
            "to"
          ]
        },
        "minItems": 0
      },
      "held": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "from",
            "to"
          ]
        },
        "minItems": 0
      },
      "rework": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "from",
            "to"
          ]
        },
        "minItems": 0
      },
      "unexamined": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "from",
            "to"
          ]
        },
        "minItems": 0
      },
      "unitRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          }
        },
        "required": [
          "owner",
          "id",
          "version"
        ]
      },
      "sampleCoverageRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "occurredAt": {
        "type": "string",
        "format": "date-time"
      },
      "recordedAt": {
        "type": "string",
        "format": "date-time"
      }
    },
    "required": [
      "schema",
      "reference",
      "sourceRef",
      "taskRef",
      "revision",
      "previousRef",
      "outputRef",
      "policyRef",
      "authorityRef",
      "accepted",
      "rejected",
      "held",
      "rework",
      "unexamined",
      "unitRef",
      "sampleCoverageRef",
      "occurredAt",
      "recordedAt"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:OutQualityDisposition"
  },
  "ExecQualityTaskResult": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "schema": {
        "const": "EXEC-QUALITY-TASK-RESULT-PROPOSED-1"
      },
      "reference": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "sourceId": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "sourceRevision": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      },
      "workOrderId": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "operationId": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "policyRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          }
        },
        "required": [
          "owner",
          "id",
          "version"
        ]
      },
      "state": {
        "type": "string",
        "enum": [
          "CREATED",
          "WAITING",
          "REJECTED"
        ]
      },
      "taskRef": {
        "oneOf": [
          {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "ref": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "owner": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 256
                  },
                  "id": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 160
                  },
                  "version": {
                    "type": "string",
                    "pattern": "^(0|[1-9][0-9]*)$"
                  }
                },
                "required": [
                  "owner",
                  "id",
                  "version"
                ]
              },
              "digest": {
                "type": "string",
                "pattern": "^[0-9a-f]{64}$"
              }
            },
            "required": [
              "ref",
              "digest"
            ]
          },
          {
            "type": "null"
          }
        ]
      },
      "canonicalSourceRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "coveredRanges": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "portionId": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "portionId",
            "from",
            "to"
          ]
        },
        "minItems": 0
      },
      "coverageRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "reasonCodes": {
        "type": "array",
        "items": {
          "type": "string",
          "minLength": 1,
          "maxLength": 256
        },
        "minItems": 0
      }
    },
    "required": [
      "schema",
      "reference",
      "sourceId",
      "sourceRevision",
      "workOrderId",
      "operationId",
      "policyRef",
      "state",
      "taskRef",
      "canonicalSourceRef",
      "coveredRanges",
      "coverageRef",
      "reasonCodes"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:ExecQualityTaskResult"
  },
  "ExecQualityDisposition": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "schema": {
        "const": "EXEC-QA-DISPOSITION-PROPOSED-1"
      },
      "reference": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "sourceRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "taskRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "revision": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      },
      "previousRef": {
        "oneOf": [
          {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "ref": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "owner": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 256
                  },
                  "id": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 160
                  },
                  "version": {
                    "type": "string",
                    "pattern": "^(0|[1-9][0-9]*)$"
                  }
                },
                "required": [
                  "owner",
                  "id",
                  "version"
                ]
              },
              "digest": {
                "type": "string",
                "pattern": "^[0-9a-f]{64}$"
              }
            },
            "required": [
              "ref",
              "digest"
            ]
          },
          {
            "type": "null"
          }
        ]
      },
      "workOrderId": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "operationId": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160
      },
      "scope": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "scopeId": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "scopeVersion": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "ranges": {
            "type": "array",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "portionId": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 160
                },
                "from": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
                },
                "to": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
                }
              },
              "required": [
                "portionId",
                "from",
                "to"
              ]
            },
            "minItems": 1
          }
        },
        "required": [
          "scopeId",
          "scopeVersion",
          "ranges"
        ]
      },
      "policyRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          }
        },
        "required": [
          "owner",
          "id",
          "version"
        ]
      },
      "authorityRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "accepted": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "portionId": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "portionId",
            "from",
            "to"
          ]
        },
        "minItems": 0
      },
      "rejected": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "portionId": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "portionId",
            "from",
            "to"
          ]
        },
        "minItems": 0
      },
      "held": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "portionId": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "portionId",
            "from",
            "to"
          ]
        },
        "minItems": 0
      },
      "rework": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "portionId": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "portionId",
            "from",
            "to"
          ]
        },
        "minItems": 0
      },
      "unexamined": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "portionId": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "portionId",
            "from",
            "to"
          ]
        },
        "minItems": 0
      },
      "unitRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          }
        },
        "required": [
          "owner",
          "id",
          "version"
        ]
      },
      "sampleCoverageRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "ref": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              }
            },
            "required": [
              "owner",
              "id",
              "version"
            ]
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "ref",
          "digest"
        ]
      },
      "occurredAt": {
        "type": "string",
        "format": "date-time"
      },
      "recordedAt": {
        "type": "string",
        "format": "date-time"
      }
    },
    "required": [
      "schema",
      "reference",
      "sourceRef",
      "taskRef",
      "revision",
      "previousRef",
      "workOrderId",
      "operationId",
      "scope",
      "policyRef",
      "authorityRef",
      "accepted",
      "rejected",
      "held",
      "rework",
      "unexamined",
      "unitRef",
      "sampleCoverageRef",
      "occurredAt",
      "recordedAt"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:ExecQualityDisposition"
  },
  "RawEvidenceHandoff": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "schema": {
        "const": "QA-IPQC-PLM-EVIDENCE-1"
      },
      "handoffId": {
        "type": "string",
        "format": "uuid"
      },
      "revision": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$"
      },
      "previousRef": {
        "oneOf": [
          {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              },
              "digest": {
                "type": "string",
                "pattern": "^[0-9a-f]{64}$"
              }
            },
            "required": [
              "owner",
              "id",
              "version",
              "digest"
            ]
          },
          {
            "type": "null"
          }
        ]
      },
      "sourceRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "owner",
          "id",
          "version",
          "digest"
        ]
      },
      "testExecutionRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "owner",
          "id",
          "version",
          "digest"
        ]
      },
      "sampleRefs": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "owner": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256
            },
            "id": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "version": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]*)$"
            },
            "digest": {
              "type": "string",
              "pattern": "^[0-9a-f]{64}$"
            }
          },
          "required": [
            "owner",
            "id",
            "version",
            "digest"
          ]
        },
        "minItems": 1
      },
      "actualBaselineRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "owner",
          "id",
          "version",
          "digest"
        ]
      },
      "methodVersionRefs": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "owner": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256
            },
            "id": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "version": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]*)$"
            },
            "digest": {
              "type": "string",
              "pattern": "^[0-9a-f]{64}$"
            }
          },
          "required": [
            "owner",
            "id",
            "version",
            "digest"
          ]
        },
        "minItems": 1
      },
      "environmentRefs": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "owner": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256
            },
            "id": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "version": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]*)$"
            },
            "digest": {
              "type": "string",
              "pattern": "^[0-9a-f]{64}$"
            }
          },
          "required": [
            "owner",
            "id",
            "version",
            "digest"
          ]
        },
        "minItems": 1
      },
      "rawRecordRefs": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "owner": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256
            },
            "id": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "version": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]*)$"
            },
            "digest": {
              "type": "string",
              "pattern": "^[0-9a-f]{64}$"
            }
          },
          "required": [
            "owner",
            "id",
            "version",
            "digest"
          ]
        },
        "minItems": 1
      },
      "contentHashes": {
        "type": "array",
        "items": {
          "type": "string",
          "pattern": "^[0-9a-f]{64}$"
        },
        "minItems": 1
      },
      "qualityTaskRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "owner",
          "id",
          "version",
          "digest"
        ]
      },
      "resultSetRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "owner",
          "id",
          "version",
          "digest"
        ]
      },
      "decisionRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "owner",
          "id",
          "version",
          "digest"
        ]
      },
      "coverageRanges": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "from": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            },
            "to": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]{0,12})(\\.[0-9]{1,8})?$"
            }
          },
          "required": [
            "from",
            "to"
          ]
        },
        "minItems": 1
      },
      "documentRefs": {
        "type": "array",
        "items": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "owner": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256
            },
            "id": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160
            },
            "version": {
              "type": "string",
              "pattern": "^(0|[1-9][0-9]*)$"
            },
            "digest": {
              "type": "string",
              "pattern": "^[0-9a-f]{64}$"
            }
          },
          "required": [
            "owner",
            "id",
            "version",
            "digest"
          ]
        },
        "minItems": 0
      },
      "occurredAt": {
        "type": "string",
        "format": "date-time"
      }
    },
    "required": [
      "schema",
      "handoffId",
      "revision",
      "previousRef",
      "sourceRef",
      "testExecutionRef",
      "sampleRefs",
      "actualBaselineRef",
      "methodVersionRefs",
      "environmentRefs",
      "rawRecordRefs",
      "contentHashes",
      "qualityTaskRef",
      "resultSetRef",
      "decisionRef",
      "coverageRanges",
      "documentRefs",
      "occurredAt"
    ],
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:RawEvidenceHandoff"
  },
  "R4FrozenPayload": {
    "type": "object",
    "properties": {
      "tenantId": {
        "type": "string",
        "format": "uuid"
      },
      "environmentId": {
        "type": "string",
        "format": "uuid"
      },
      "mode": {
        "type": "string",
        "enum": [
          "REAL",
          "DEMO"
        ]
      },
      "payloadId": {
        "type": "string",
        "format": "uuid",
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:UUID"
      },
      "payloadType": {
        "type": "string",
        "enum": [
          "MES_OUTPUT_QUALITY_TASK_RESULT_V1",
          "MES_OUTPUT_QUALITY_DISPOSITION_V1",
          "MES_EXEC_QUALITY_TASK_RESULT_PROPOSED_V1",
          "MES_EXEC_QUALITY_DISPOSITION_PROPOSED_V1",
          "PLM_RAW_EVIDENCE_HANDOFF_V1"
        ],
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:PayloadType"
      },
      "schemaType": {
        "oneOf": [
          {
            "const": "OutQualityTaskResult"
          },
          {
            "const": "OutQualityDisposition"
          },
          {
            "const": "ExecQualityTaskResult"
          },
          {
            "const": "ExecQualityDisposition"
          },
          {
            "const": "RawEvidenceHandoff"
          }
        ]
      },
      "payloadDigest": {
        "type": "string",
        "pattern": "^[0-9a-f]{64}$",
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:Digest"
      },
      "registrationRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "owner",
          "id",
          "version",
          "digest"
        ],
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapEvidence"
      },
      "frozenAt": {
        "type": "string",
        "format": "date-time",
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:Time"
      }
    },
    "required": [
      "tenantId",
      "environmentId",
      "mode",
      "payloadId",
      "payloadType",
      "schemaType",
      "payloadDigest",
      "registrationRef",
      "frozenAt"
    ],
    "additionalProperties": false
  },
  "R4QapFullCountPlanContent": {
    "type": "object",
    "properties": {
      "schema": {
        "const": "QAP-PLAN-1"
      },
      "planId": {
        "type": "string",
        "minLength": 1,
        "maxLength": 160,
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:Id"
      },
      "version": {
        "type": "string",
        "pattern": "^(0|[1-9][0-9]*)$",
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:Ver"
      },
      "previousRef": {
        "oneOf": [
          {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "owner": {
                "type": "string",
                "minLength": 1,
                "maxLength": 256
              },
              "id": {
                "type": "string",
                "minLength": 1,
                "maxLength": 160
              },
              "version": {
                "type": "string",
                "pattern": "^(0|[1-9][0-9]*)$"
              },
              "digest": {
                "type": "string",
                "pattern": "^[0-9a-f]{64}$"
              }
            },
            "required": [
              "owner",
              "id",
              "version",
              "digest"
            ],
            "$schema": "https://json-schema.org/draft/2020-12/schema",
            "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapEvidence"
          },
          {
            "type": "null"
          }
        ]
      },
      "name": {
        "type": "string",
        "minLength": 1,
        "maxLength": 256,
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:ShortText"
      },
      "stage": {
        "type": "string",
        "enum": [
          "IPQC",
          "FQC",
          "TRIAL_INSPECTION"
        ],
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:Stage"
      },
      "technicalBindingRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "owner",
          "id",
          "version",
          "digest"
        ],
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapEvidence"
      },
      "characteristics": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "requirementId": {
              "type": "string",
              "minLength": 1,
              "maxLength": 160,
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:Id"
            },
            "methodRef": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "owner": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 256
                },
                "id": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 160
                },
                "version": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]*)$"
                },
                "digest": {
                  "type": "string",
                  "pattern": "^[0-9a-f]{64}$"
                }
              },
              "required": [
                "owner",
                "id",
                "version",
                "digest"
              ],
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapEvidence"
            },
            "specificationRef": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "owner": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 256
                },
                "id": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 160
                },
                "version": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]*)$"
                },
                "digest": {
                  "type": "string",
                  "pattern": "^[0-9a-f]{64}$"
                }
              },
              "required": [
                "owner",
                "id",
                "version",
                "digest"
              ],
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapEvidence"
            },
            "measurementUnitRef": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "id": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 256
                },
                "version": {
                  "type": "string",
                  "pattern": "^(0|[1-9][0-9]*)$"
                }
              },
              "required": [
                "id",
                "version"
              ],
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapUom"
            },
            "rule": {
              "type": "object",
              "properties": {
                "kind": {
                  "const": "FULL_COUNT"
                },
                "unitRef": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "id": {
                      "type": "string",
                      "minLength": 1,
                      "maxLength": 256
                    },
                    "version": {
                      "type": "string",
                      "pattern": "^(0|[1-9][0-9]*)$"
                    }
                  },
                  "required": [
                    "id",
                    "version"
                  ],
                  "$schema": "https://json-schema.org/draft/2020-12/schema",
                  "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapUom"
                }
              },
              "required": [
                "kind",
                "unitRef"
              ],
              "additionalProperties": false
            },
            "required": {
              "const": true
            }
          },
          "required": [
            "requirementId",
            "methodRef",
            "specificationRef",
            "measurementUnitRef",
            "rule",
            "required"
          ],
          "additionalProperties": false
        }
      },
      "grouping": {
        "const": "ONE_PHYSICAL_DOMAIN_AND_LOT"
      },
      "reinspection": {
        "const": "EXPLICIT_TASK_OWNER_SUCCESSOR"
      },
      "standardApprovalRef": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "owner": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "id": {
            "type": "string",
            "minLength": 1,
            "maxLength": 160
          },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)$"
          },
          "digest": {
            "type": "string",
            "pattern": "^[0-9a-f]{64}$"
          }
        },
        "required": [
          "owner",
          "id",
          "version",
          "digest"
        ],
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapEvidence"
      },
      "validity": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "from": {
            "type": "string",
            "format": "date-time"
          },
          "to": {
            "oneOf": [
              {
                "type": "string",
                "format": "date-time"
              },
              {
                "type": "null"
              }
            ]
          },
          "noEndAuthorityRef": {
            "oneOf": [
              {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "owner": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 256
                  },
                  "id": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 160
                  },
                  "version": {
                    "type": "string",
                    "pattern": "^(0|[1-9][0-9]*)$"
                  },
                  "digest": {
                    "type": "string",
                    "pattern": "^[0-9a-f]{64}$"
                  }
                },
                "required": [
                  "owner",
                  "id",
                  "version",
                  "digest"
                ]
              },
              {
                "type": "null"
              }
            ]
          }
        },
        "required": [
          "from",
          "to",
          "noEndAuthorityRef"
        ],
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:QapValidity"
      },
      "reason": {
        "type": "string",
        "minLength": 1,
        "maxLength": 256,
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "urn:cp6:qa-ipqc:recovery-r4-systemic:ShortText"
      }
    },
    "required": [
      "schema",
      "planId",
      "version",
      "previousRef",
      "name",
      "stage",
      "technicalBindingRef",
      "characteristics",
      "grouping",
      "reinspection",
      "standardApprovalRef",
      "validity",
      "reason"
    ],
    "additionalProperties": false
  }
}

export const payloadRoutes = {
  "MES_OUTPUT_QUALITY_TASK_RESULT_V1": "OutQualityTaskResult",
  "MES_OUTPUT_QUALITY_DISPOSITION_V1": "OutQualityDisposition",
  "MES_EXEC_QUALITY_TASK_RESULT_PROPOSED_V1": "ExecQualityTaskResult",
  "MES_EXEC_QUALITY_DISPOSITION_PROPOSED_V1": "ExecQualityDisposition",
  "PLM_RAW_EVIDENCE_HANDOFF_V1": "RawEvidenceHandoff"
} as const
