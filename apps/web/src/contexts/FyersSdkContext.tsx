"use client";

import { createContext, useContext } from "react";

export type FyersSdkStatus = "loading" | "loaded" | "error";

export interface FyersSdkContextValue {
  status: FyersSdkStatus;
  error?: Error;
}

export const FyersSdkContext = createContext<FyersSdkContextValue>({
  status: "loading",
});

export function useFyersSdk(): FyersSdkContextValue {
  return useContext(FyersSdkContext);
}
