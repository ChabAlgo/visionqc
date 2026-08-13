import { GoogleGenAI, Type } from '@google/genai';

// IMPORTANT:
// Do not create GoogleGenAI at module load time.
// If apiKey is undefined, @google/genai throws immediately in browser and the whole UI becomes blank.
// We initialize it only when the user clicks AI SUGGEST.
const getGeminiApiKey = (): string => {
  const g = globalThis as any;
  return (
    g.__GEMINI_API_KEY__ ||
    g.GEMINI_API_KEY ||
    ((import.meta as any).env && ((import.meta as any).env.VITE_GEMINI_API_KEY || (import.meta as any).env.GEMINI_API_KEY)) ||
    ''
  );
};

const VALID_STATUS_IDS = ['OK', 'CRACK', 'ETC', 'FOIL_DAMAGE', 'MAIN_WELDING', 'NO_TAB', 'SPATTER', 'TRIMMING'];

export const analyzeImage = async (base64Image: string, fileName: string) => {
  const apiKey = getGeminiApiKey();
  if (!apiKey) {
    console.warn('[VisionQC] Gemini API key is not set. AI SUGGEST is disabled, but manual classification/export still works.');
    return null;
  }

  const ai = new GoogleGenAI({ apiKey });

  try {
    const response = await ai.models.generateContent({
      model: 'gemini-3-pro-preview',
      contents: {
        parts: [
          {
            inlineData: {
              mimeType: 'image/jpeg',
              data: base64Image
            }
          },
          {
            text: `Analyze this manufacturing image (file: ${fileName}) for quality control.
            Classify it into one of the following categories:
            - OK: No defects.
            - CRACK: Structural cracks visible.
            - FOIL_DAMAGE: Damage to the foil layers.
            - MAIN_WELDING: Defects in the primary welding area.
            - NO_TAB: Missing tab components.
            - SPATTER: Welding spatter or debris.
            - TRIMMING: Poor trimming or edge defects.
            - ETC: Other miscellaneous defects.

            If a defect is found, strictly provide the normalized coordinates [ymin, xmin, ymax, xmax] (0-1000 scale) for the specific area(s) where the defect is visible to generate a heatmap.

            Return the result in JSON format.`
          }
        ]
      },
      config: {
        responseMimeType: 'application/json',
        responseSchema: {
          type: Type.OBJECT,
          properties: {
            status: { type: Type.STRING },
            reason: { type: Type.STRING },
            confidence: { type: Type.NUMBER },
            regions: {
              type: Type.ARRAY,
              items: {
                type: Type.OBJECT,
                properties: {
                  ymin: { type: Type.NUMBER },
                  xmin: { type: Type.NUMBER },
                  ymax: { type: Type.NUMBER },
                  xmax: { type: Type.NUMBER }
                },
                required: ['ymin', 'xmin', 'ymax', 'xmax']
              }
            }
          },
          required: ['status', 'reason', 'confidence']
        }
      }
    });

    const text = response.text;
    if (!text) throw new Error('Received empty response from Gemini API.');

    const result = JSON.parse(text.trim());
    return {
      status: VALID_STATUS_IDS.includes(result.status) ? result.status : 'ETC',
      reason: result.reason,
      confidence: result.confidence,
      regions: result.regions || []
    };
  } catch (error) {
    console.error('Gemini Analysis Error:', error);
    return null;
  }
};
