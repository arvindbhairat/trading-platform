import coreWebVitalsConfig from "eslint-config-next/core-web-vitals";
import typescriptConfig from "eslint-config-next/typescript";

const eslintConfig = [
  {
    ignores: ["next-env.d.ts", ".next/**"],
  },
  ...coreWebVitalsConfig,
  ...typescriptConfig,
];

export default eslintConfig;
