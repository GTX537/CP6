import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

const clientPathPatterns = [
  /^\/api\/client-auth\//,
  /^\/api\/client\/bootstrap$/,
  /^\/api\/client\/devices\//,
  /^\/api\/v2\/wms\/tasks(?:\/|$|\?)/,
  /^\/api\/v2\/wms\/label-jobs(?:\/|$|\?)/,
];

export function isClientPath(path) {
  return clientPathPatterns.some((pattern) => pattern.test(path));
}

function schemaNameFromReference(reference) {
  const prefix = "#/components/schemas/";
  if (typeof reference !== "string" || !reference.startsWith(prefix)) {
    return null;
  }

  return decodeURIComponent(reference.slice(prefix.length))
    .replaceAll("~1", "/")
    .replaceAll("~0", "~");
}

function collectSchemaReferences(value, result) {
  if (Array.isArray(value)) {
    for (const item of value) {
      collectSchemaReferences(item, result);
    }
    return;
  }

  if (value === null || typeof value !== "object") {
    return;
  }

  const schemaName = schemaNameFromReference(value.$ref);
  if (schemaName !== null) {
    result.add(schemaName);
  }
  for (const nested of Object.values(value)) {
    collectSchemaReferences(nested, result);
  }
}

export function buildClientSurface(document) {
  const selectedPaths = Object.fromEntries(
    Object.entries(document.paths ?? {}).filter(([path]) => isClientPath(path)),
  );
  const allSchemas = document.components?.schemas ?? {};
  const pending = new Set();
  collectSchemaReferences(selectedPaths, pending);

  const selectedSchemas = Object.create(null);
  while (pending.size > 0) {
    const [schemaName] = pending;
    pending.delete(schemaName);
    if (Object.hasOwn(selectedSchemas, schemaName)) {
      continue;
    }
    if (!Object.hasOwn(allSchemas, schemaName)) {
      throw new Error(`Referenced OpenAPI schema is missing: ${schemaName}`);
    }

    selectedSchemas[schemaName] = allSchemas[schemaName];
    collectSchemaReferences(allSchemas[schemaName], pending);
  }

  return { paths: selectedPaths, schemas: selectedSchemas };
}

function sortJson(value) {
  if (Array.isArray(value)) {
    return value.map(sortJson);
  }
  if (value === null || typeof value !== "object") {
    return value;
  }

  return Object.fromEntries(
    Object.keys(value)
      .sort()
      .map((key) => [key, sortJson(value[key])]),
  );
}

export function hashClientSurface(document) {
  const canonical = JSON.stringify(sortJson(buildClientSurface(document)));
  return createHash("sha256").update(canonical, "utf8").digest("hex").toUpperCase();
}

function parseArguments(arguments_) {
  const options = { update: false };
  for (let index = 0; index < arguments_.length; index += 1) {
    const argument = arguments_[index];
    if (argument === "--update") {
      options.update = true;
      continue;
    }
    if (argument === "--swagger-url" || argument === "--hash-file") {
      const value = arguments_[index + 1];
      if (!value) {
        throw new Error(`Missing value for ${argument}`);
      }
      options[argument === "--swagger-url" ? "swaggerUrl" : "hashFile"] = value;
      index += 1;
      continue;
    }
    throw new Error(`Unknown argument: ${argument}`);
  }

  if (!options.swaggerUrl || !options.hashFile) {
    throw new Error("--swagger-url and --hash-file are required");
  }
  return options;
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const response = await fetch(options.swaggerUrl);
  if (!response.ok) {
    throw new Error(`OpenAPI request failed: ${response.status} ${response.statusText}`);
  }
  const hash = hashClientSurface(await response.json());

  if (options.update) {
    await writeFile(options.hashFile, hash, "utf8");
    console.log(`Updated client surface hash: ${hash}`);
    return;
  }

  const expected = (await readFile(options.hashFile, "utf8")).trim();
  if (expected !== hash) {
    throw new Error(
      `OpenAPI client drift detected. Expected ${expected}, actual ${hash}. ` +
        "Review the client contract and rerun with -Update.",
    );
  }
  console.log(`OpenAPI client surface is in sync: ${hash}`);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
