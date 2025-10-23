export interface LogData {
  startIndex: string;
  endIndex: string;
  curveSpecifications: CurveSpecification[];
  data: LogDataRow[];
  aiServiceResult: AiServiceResult;
}

export interface CurveSpecification {
  mnemonic: string;
  unit: string;
}

export interface LogDataRow {
  [key: string]: number | string | boolean;
}

export interface AiServiceResult {
  errorMessage: string;
  isSuccess: boolean;
  generatedPythonCode: string;
  modelExecTimeMs: number;
  pythonExecTimeMs: number;
  fileId: string;
  usedModelName: string;
}
