module.exports = {
  root: true,
  env: {
    browser: true,
    es2022: true,
    node: true,
  },
  parserOptions: {
    ecmaVersion: "latest",
    sourceType: "module",
    ecmaFeatures: { jsx: true },
  },
  // JavaScript & React code quality rules aligned with SonarJS / SonarQube JS profile
  rules: {
    "no-unused-vars": ["warn", { argsIgnorePattern: "^_", varsIgnorePattern: "^[A-Z_]" }],
    "no-undef": "error",
    "no-unreachable": "error",
    "no-duplicate-imports": "error",
    "no-constant-condition": "warn",
    "no-console": ["warn", { allow: ["warn", "error"] }],
    "no-debugger": "error",
    "no-eval": "error",
    "no-implied-eval": "error",
    "no-var": "error",
    "prefer-const": "warn",
    eqeqeq: ["error", "always", { null: "ignore" }],
    complexity: ["warn", { max: 15 }],
    "max-depth": ["warn", 4],
    "max-params": ["warn", 5],
  },
};

