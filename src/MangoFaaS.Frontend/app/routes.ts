import { type RouteConfig, index, route } from "@react-router/dev/routes"

export default [
  index("routes/home.tsx"),
  route("functions", "routes/functions.tsx"),
  route("routes", "routes/routes.tsx"),
  route("runtimes", "routes/runtimes.tsx"),
  route("secrets", "routes/secrets.tsx"),
] satisfies RouteConfig
