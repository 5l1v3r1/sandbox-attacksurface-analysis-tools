﻿//  Copyright 2020 Google Inc. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;

namespace NtApiDotNet.Win32.Security
{
    /// <summary>
    /// Class to represent a server authentication context.
    /// </summary>
    public sealed class ServerAuthenticationContext : IDisposable
    {
        private readonly CredentialHandle _creds;
        private readonly SecHandle _context;
        private readonly AcceptContextReqFlags _req_flags;
        private readonly SecDataRep _data_rep;

        /// <summary>
        /// The current authentication token.
        /// </summary>
        public byte[] Token { get; private set; }

        /// <summary>
        /// Whether the authentication is done.
        /// </summary>
        public bool Done { get; private set; }

        /// <summary>
        /// Current status flags.
        /// </summary>
        public AcceptContextRetFlags Flags { get; private set; }

        /// <summary>
        /// Expiry of the authentication.
        /// </summary>
        public long Expiry { get; private set; }

        /// <summary>
        /// Get an access token for the authenticated user.
        /// </summary>
        /// <returns>The user's access token.</returns>
        public NtToken GetAccessToken()
        {
            SecurityNativeMethods.QuerySecurityContextToken(_context, out SafeKernelObjectHandle token).CheckResult();
            return NtToken.FromHandle(token);
        }

        /// <summary>
        /// Impersonate the security context.
        /// </summary>
        /// <returns>The disposable context to revert the impersonation.</returns>
        public AuthenticationImpersonationContext Impersonate()
        {
            SecurityNativeMethods.ImpersonateSecurityContext(_context).CheckResult();
            return new AuthenticationImpersonationContext(_context);
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="creds">Credential handle.</param>
        /// <param name="req_attributes">Request attribute flags.</param>
        /// <param name="data_rep">Data representation.</param>
        /// <param name="token">Initial authentication token.</param>
        public ServerAuthenticationContext(CredentialHandle creds, byte[] token,
            AcceptContextReqFlags req_attributes, SecDataRep data_rep)
        {
            _creds = creds;
            _context = new SecHandle();
            _req_flags = req_attributes & ~AcceptContextReqFlags.AllocateMemory;
            _data_rep = data_rep;
            Done = GenServerContext(true, token);
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="creds">Credential handle.</param>
        /// <param name="token">Initial authentication token.</param>
        public ServerAuthenticationContext(CredentialHandle creds, byte[] token) : this(creds, token,
            AcceptContextReqFlags.None, SecDataRep.Native)
        {
        }

        /// <summary>
        /// Continue the authentication with the client token.
        /// </summary>
        /// <param name="token">The client token to continue authentication.</param>
        public void Continue(byte[] token)
        {
            Done = GenServerContext(false, token);
        }

        private bool GenServerContext(
            bool new_context, byte[] token)
        {
            using (DisposableList list = new DisposableList())
            {
                SecBuffer out_sec_buffer = list.AddResource(new SecBuffer(SecBufferType.Token, 8192));
                SecBufferDesc out_buffer_desc = list.AddResource(new SecBufferDesc(out_sec_buffer));
                SecBuffer in_sec_buffer = list.AddResource(new SecBuffer(SecBufferType.Token, token));
                SecBufferDesc in_buffer_desc = list.AddResource(new SecBufferDesc(in_sec_buffer));

                LargeInteger expiry = new LargeInteger();
                SecStatusCode result = SecurityNativeMethods.AcceptSecurityContext(_creds.CredHandle, new_context ? null : _context,
                    in_buffer_desc, _req_flags, _data_rep, _context, out_buffer_desc, out AcceptContextRetFlags context_attr, expiry).CheckResult();
                Flags = context_attr;
                Expiry = expiry.QuadPart;
                if (result == SecStatusCode.CompleteNeeded || result == SecStatusCode.CompleteAndContinue)
                {
                    SecurityNativeMethods.CompleteAuthToken(_context, out_buffer_desc).CheckResult();
                }

                Token = out_buffer_desc.ToArray()[0].ToArray();
                return !(result == SecStatusCode.ContinueNeeded || result == SecStatusCode.CompleteAndContinue);
            }
        }

        void IDisposable.Dispose()
        {
            SecurityNativeMethods.DeleteSecurityContext(_context);
        }
    }
}
