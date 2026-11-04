export interface paths {
    "/health/live": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** @description Local infrastructure probe only; returns no user data or diagnostic details. */
        get: operations["GetLiveness"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/guest": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Create an isolated 24-hour demo account and issue a guest token. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["GuestSessionResponse"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List the authenticated user's runs within inclusive UTC dates. */
        get: {
            parameters: {
                query: {
                    from: string;
                    to: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RunResponse"][];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        /** Create a run owned by the authenticated user. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["RunInput"];
                };
            };
            responses: {
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RunResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Get one of the authenticated user's runs. */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RunResponse"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        /** Replace one of the authenticated user's runs. */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["RunInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RunResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        /** Delete one of the authenticated user's runs. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/{id}/track": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProcessedTrack"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query: {
                    planned: boolean;
                };
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/gpx+xml": components["schemas"]["Stream"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProcessedTrack"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/{id}/track/original": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/gpx+xml": components["schemas"]["Stream"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/maps/token": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MapTokenResponse"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/maps/geocode": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query: {
                    query: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LocationResult"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
}
export type webhooks = Record<string, never>;
export interface components {
    schemas: {
        ElevationSample: {
            /** Format: double */
            distanceMeters: number;
            /** Format: double */
            elevationMeters: null | number | string;
        };
        GuestSessionResponse: {
            accessToken: string;
            /** Format: date-time */
            expiresAtUtc: string;
        };
        HttpValidationProblemDetails: {
            type?: null | string;
            title?: null | string;
            /** Format: int32 */
            status?: null | number | string;
            detail?: null | string;
            instance?: null | string;
            errors?: {
                [key: string]: string[];
            };
        };
        LivenessResponse: {
            status: string;
        };
        LocationInput: {
            displayName: string;
            /** Format: double */
            latitude: number | string;
            /** Format: double */
            longitude: number | string;
        };
        LocationResponse: {
            displayName: string;
            /** Format: double */
            latitude: number | string;
            /** Format: double */
            longitude: number | string;
        };
        LocationResult: {
            displayName: string;
            /** Format: double */
            latitude: number;
            /** Format: double */
            longitude: number;
        };
        MapTokenResponse: {
            clientId: string;
            accessToken: string;
            /** Format: date-time */
            expiresAtUtc: string;
        };
        ProcessedTrack: {
            type: string;
            features: components["schemas"]["TrackFeature"][];
            elevation: components["schemas"]["ElevationSample"][];
            isPlannedCourse: boolean;
        };
        RaceDetailsInput: {
            raceName?: null | string;
            officialDistanceCategory?: null | string;
            bibNumber?: null | string;
            /** Format: int32 */
            targetTimeSeconds?: null | number | string;
            /** Format: int32 */
            resultTimeSeconds?: null | number | string;
            /** Format: int32 */
            overallPlace?: null | number | string;
            /** Format: int32 */
            categoryPlace?: null | number | string;
            resultsUrl?: null | string;
        };
        RaceDetailsResponse: {
            raceName: null | string;
            officialDistanceCategory: null | string;
            bibNumber: null | string;
            /** Format: int32 */
            targetTimeSeconds: null | number | string;
            /** Format: int32 */
            resultTimeSeconds: null | number | string;
            /** Format: int32 */
            overallPlace: null | number | string;
            /** Format: int32 */
            categoryPlace: null | number | string;
            resultsUrl: null | string;
        };
        RatingInput: {
            /** Format: int32 */
            stars: number | string;
            comment?: null | string;
        };
        RatingResponse: {
            /** Format: int32 */
            stars: number | string;
            comment: null | string;
        };
        RunInput: {
            /** Format: date-time */
            startAtUtc: string;
            status: components["schemas"]["RunStatus"];
            runTypeCode: string;
            title: string;
            notes?: null | string;
            /** Format: double */
            distanceMeters?: null | number | string;
            /** Format: int32 */
            durationSeconds?: null | number | string;
            /** Format: double */
            elevationGainMeters?: null | number | string;
            /** Format: double */
            elevationLossMeters?: null | number | string;
            /** Format: int32 */
            averageHeartRateBpm?: null | number | string;
            /** Format: int32 */
            maximumHeartRateBpm?: null | number | string;
            /** Format: int32 */
            perceivedEffort?: null | number | string;
            shoesOrGear?: null | string;
            weather?: null | string;
            surface?: null | string;
            location?: null | components["schemas"]["LocationInput"];
            raceDetails?: null | components["schemas"]["RaceDetailsInput"];
            rating?: null | components["schemas"]["RatingInput"];
        };
        RunResponse: {
            /** Format: uuid */
            id: string;
            /** Format: date-time */
            startAtUtc: string;
            status: components["schemas"]["RunStatus"];
            runTypeCode: string;
            runTypeName: string;
            title: string;
            notes: null | string;
            /** Format: double */
            distanceMeters: null | number | string;
            /** Format: int32 */
            durationSeconds: null | number | string;
            /** Format: double */
            elevationGainMeters: null | number | string;
            /** Format: double */
            elevationLossMeters: null | number | string;
            /** Format: int32 */
            averageHeartRateBpm: null | number | string;
            /** Format: int32 */
            maximumHeartRateBpm: null | number | string;
            /** Format: int32 */
            perceivedEffort: null | number | string;
            shoesOrGear: null | string;
            weather: null | string;
            surface: null | string;
            location: null | components["schemas"]["LocationResponse"];
            raceDetails: null | components["schemas"]["RaceDetailsResponse"];
            rating: null | components["schemas"]["RatingResponse"];
        };
        /** @enum {unknown} */
        RunStatus: "Planned" | "Completed";
        /** Format: binary */
        Stream: Blob;
        TrackFeature: {
            type: string;
            geometry: components["schemas"]["TrackGeometry"];
            properties: components["schemas"]["TrackMetrics"];
        };
        TrackGeometry: {
            type: string;
            coordinates: (number | string)[][][];
        };
        TrackMetrics: {
            /** Format: double */
            distanceMeters: number;
            /** Format: double */
            elevationGainMeters: number;
            /** Format: double */
            elevationLossMeters: number;
            /** Format: int32 */
            durationSeconds: null | number | string;
            /** Format: double */
            startLatitude: number;
            /** Format: double */
            startLongitude: number;
        };
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
    GetLiveness: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["LivenessResponse"];
                };
            };
        };
    };
}
