--
-- PostgreSQL database dump
--

\restrict 3Kd36I6mnpxXOoD2hfNIdg2ZZLzjtXuOqvyiYgCofyGSwAsmb5BWtEhs2e1rhud

-- Dumped from database version 18.4
-- Dumped by pg_dump version 18.4

-- Started on 2026-10-03 14:49:10

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- TOC entry 4 (class 2615 OID 2200)
-- Name: public; Type: SCHEMA; Schema: -; Owner: pg_database_owner
--

CREATE SCHEMA public;


ALTER SCHEMA public OWNER TO pg_database_owner;

--
-- TOC entry 5188 (class 0 OID 0)
-- Dependencies: 4
-- Name: SCHEMA public; Type: COMMENT; Schema: -; Owner: pg_database_owner
--

COMMENT ON SCHEMA public IS 'standard public schema';


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- TOC entry 226 (class 1259 OID 25465)
-- Name: lut_board_type; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.lut_board_type (
    board_type_id bigint NOT NULL,
    board_code character varying(50) NOT NULL,
    board_name character varying(150) NOT NULL,
    description text,
    isactive boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


ALTER TABLE public.lut_board_type OWNER TO postgres;

--
-- TOC entry 225 (class 1259 OID 25464)
-- Name: lut_board_type_board_type_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.lut_board_type ALTER COLUMN board_type_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.lut_board_type_board_type_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 222 (class 1259 OID 25432)
-- Name: lut_city; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.lut_city (
    cid integer NOT NULL,
    sid integer NOT NULL,
    did integer NOT NULL,
    city_id integer NOT NULL,
    city_name character varying(250) NOT NULL
);


ALTER TABLE public.lut_city OWNER TO postgres;

--
-- TOC entry 219 (class 1259 OID 25392)
-- Name: lut_country; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.lut_country (
    cid integer NOT NULL,
    cname character varying(250) NOT NULL
);


ALTER TABLE public.lut_country OWNER TO postgres;

--
-- TOC entry 221 (class 1259 OID 25416)
-- Name: lut_district; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.lut_district (
    cid integer NOT NULL,
    sid integer NOT NULL,
    did integer NOT NULL,
    dname character varying(250) NOT NULL
);


ALTER TABLE public.lut_district OWNER TO postgres;

--
-- TOC entry 232 (class 1259 OID 25628)
-- Name: lut_roles; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.lut_roles (
    role_id bigint NOT NULL,
    role_name character varying(100) NOT NULL,
    role_code character varying(50) NOT NULL,
    description text,
    status_id integer,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


ALTER TABLE public.lut_roles OWNER TO postgres;

--
-- TOC entry 231 (class 1259 OID 25627)
-- Name: lut_roles_role_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.lut_roles ALTER COLUMN role_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.lut_roles_role_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 230 (class 1259 OID 25507)
-- Name: lut_school_level; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.lut_school_level (
    school_level_id bigint NOT NULL,
    school_level_code character varying(50) NOT NULL,
    school_level_name character varying(150) NOT NULL,
    description text,
    isactive boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


ALTER TABLE public.lut_school_level OWNER TO postgres;

--
-- TOC entry 229 (class 1259 OID 25506)
-- Name: lut_school_level_school_level_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.lut_school_level ALTER COLUMN school_level_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.lut_school_level_school_level_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 228 (class 1259 OID 25486)
-- Name: lut_school_type; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.lut_school_type (
    school_type_id bigint NOT NULL,
    school_type_code character varying(50) NOT NULL,
    school_type_name character varying(150) NOT NULL,
    description text,
    isactive boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


ALTER TABLE public.lut_school_type OWNER TO postgres;

--
-- TOC entry 227 (class 1259 OID 25485)
-- Name: lut_school_type_school_type_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.lut_school_type ALTER COLUMN school_type_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.lut_school_type_school_type_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 220 (class 1259 OID 25401)
-- Name: lut_state; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.lut_state (
    cid integer NOT NULL,
    sid integer NOT NULL,
    sname character varying(250) NOT NULL
);


ALTER TABLE public.lut_state OWNER TO postgres;

--
-- TOC entry 224 (class 1259 OID 25450)
-- Name: lut_status; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.lut_status (
    sid integer NOT NULL,
    sname character varying(250) NOT NULL,
    stype character varying(250) NOT NULL,
    isactive boolean DEFAULT true NOT NULL
);


ALTER TABLE public.lut_status OWNER TO postgres;

--
-- TOC entry 223 (class 1259 OID 25449)
-- Name: lut_status_sid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.lut_status ALTER COLUMN sid ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.lut_status_sid_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 240 (class 1259 OID 27381)
-- Name: school_no_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.school_no_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.school_no_seq OWNER TO postgres;

--
-- TOC entry 239 (class 1259 OID 27328)
-- Name: tb_school_contacts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.tb_school_contacts (
    contact_id bigint NOT NULL,
    school_id character varying(100) NOT NULL,
    contact_type character varying(100) NOT NULL,
    contact_name character varying(200) NOT NULL,
    designation character varying(150),
    email character varying(150),
    mobile_number character varying(20),
    alternate_mobile_number character varying(20),
    address_line1 character varying(250),
    address_line2 character varying(250),
    country_id integer,
    state_id integer,
    district_id integer,
    city_id integer,
    pincode character varying(20),
    is_primary boolean DEFAULT false NOT NULL,
    status_id integer,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


ALTER TABLE public.tb_school_contacts OWNER TO postgres;

--
-- TOC entry 238 (class 1259 OID 27327)
-- Name: tb_school_contacts_contact_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.tb_school_contacts ALTER COLUMN contact_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.tb_school_contacts_contact_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 237 (class 1259 OID 27261)
-- Name: tb_schools; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.tb_schools (
    school_id character varying(100) NOT NULL,
    school_code character varying(100) NOT NULL,
    school_name character varying(250) NOT NULL,
    school_short_name character varying(100),
    school_type_id bigint,
    school_level_id bigint,
    board_type_id bigint,
    school_establish_year smallint,
    school_gstin character varying(15),
    school_pan character varying(10),
    country_id integer,
    state_id integer,
    district_id integer,
    city_id integer,
    address_line1 character varying(250),
    address_line2 character varying(250),
    pincode character varying(20),
    email character varying(150),
    mobile_number character varying(20),
    website character varying(200),
    logo_url text,
    subscription_plan_id bigint,
    subscription_start_date date,
    subscription_end_date date,
    subscription_status_id integer,
    school_status_id integer,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT chk_tb_schools_establish_year CHECK (((school_establish_year IS NULL) OR ((school_establish_year >= 1800) AND ((school_establish_year)::numeric <= EXTRACT(year FROM CURRENT_DATE)))))
);


ALTER TABLE public.tb_schools OWNER TO postgres;

--
-- TOC entry 236 (class 1259 OID 25682)
-- Name: tb_user_roles; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.tb_user_roles (
    user_role_id bigint NOT NULL,
    user_id bigint NOT NULL,
    role_id bigint NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


ALTER TABLE public.tb_user_roles OWNER TO postgres;

--
-- TOC entry 235 (class 1259 OID 25681)
-- Name: tb_user_roles_user_role_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.tb_user_roles ALTER COLUMN user_role_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.tb_user_roles_user_role_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 234 (class 1259 OID 25652)
-- Name: tb_users; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.tb_users (
    user_id bigint NOT NULL,
    school_id character varying(100),
    user_type character varying(30) NOT NULL,
    org_user_id character varying(50),
    username character varying(100),
    email character varying(150),
    mobile_number character varying(20),
    password_hash text,
    is_first_login boolean DEFAULT true NOT NULL,
    is_email_verified boolean DEFAULT false NOT NULL,
    is_mobile_verified boolean DEFAULT false NOT NULL,
    status_id integer,
    last_login_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


ALTER TABLE public.tb_users OWNER TO postgres;

--
-- TOC entry 233 (class 1259 OID 25651)
-- Name: tb_users_user_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.tb_users ALTER COLUMN user_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.tb_users_user_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 4957 (class 2606 OID 25480)
-- Name: lut_board_type lut_board_type_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_board_type
    ADD CONSTRAINT lut_board_type_pkey PRIMARY KEY (board_type_id);


--
-- TOC entry 4937 (class 2606 OID 25398)
-- Name: lut_country lut_country_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_country
    ADD CONSTRAINT lut_country_pkey PRIMARY KEY (cid);


--
-- TOC entry 4975 (class 2606 OID 25641)
-- Name: lut_roles lut_roles_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_roles
    ADD CONSTRAINT lut_roles_pkey PRIMARY KEY (role_id);


--
-- TOC entry 4969 (class 2606 OID 25522)
-- Name: lut_school_level lut_school_level_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_school_level
    ADD CONSTRAINT lut_school_level_pkey PRIMARY KEY (school_level_id);


--
-- TOC entry 4963 (class 2606 OID 25501)
-- Name: lut_school_type lut_school_type_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_school_type
    ADD CONSTRAINT lut_school_type_pkey PRIMARY KEY (school_type_id);


--
-- TOC entry 4953 (class 2606 OID 25461)
-- Name: lut_status lut_status_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_status
    ADD CONSTRAINT lut_status_pkey PRIMARY KEY (sid);


--
-- TOC entry 4949 (class 2606 OID 25441)
-- Name: lut_city pk_lut_city; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_city
    ADD CONSTRAINT pk_lut_city PRIMARY KEY (cid, sid, did, city_id);


--
-- TOC entry 4945 (class 2606 OID 25424)
-- Name: lut_district pk_lut_district; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_district
    ADD CONSTRAINT pk_lut_district PRIMARY KEY (cid, sid, did);


--
-- TOC entry 4941 (class 2606 OID 25408)
-- Name: lut_state pk_lut_state; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_state
    ADD CONSTRAINT pk_lut_state PRIMARY KEY (cid, sid);


--
-- TOC entry 5013 (class 2606 OID 27344)
-- Name: tb_school_contacts tb_school_contacts_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_school_contacts
    ADD CONSTRAINT tb_school_contacts_pkey PRIMARY KEY (contact_id);


--
-- TOC entry 4999 (class 2606 OID 27275)
-- Name: tb_schools tb_schools_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT tb_schools_pkey PRIMARY KEY (school_id);


--
-- TOC entry 4995 (class 2606 OID 25691)
-- Name: tb_user_roles tb_user_roles_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_user_roles
    ADD CONSTRAINT tb_user_roles_pkey PRIMARY KEY (user_role_id);


--
-- TOC entry 4988 (class 2606 OID 25670)
-- Name: tb_users tb_users_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_users
    ADD CONSTRAINT tb_users_pkey PRIMARY KEY (user_id);


--
-- TOC entry 4959 (class 2606 OID 25482)
-- Name: lut_board_type uq_lut_board_type_code; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_board_type
    ADD CONSTRAINT uq_lut_board_type_code UNIQUE (board_code);


--
-- TOC entry 4961 (class 2606 OID 25484)
-- Name: lut_board_type uq_lut_board_type_name; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_board_type
    ADD CONSTRAINT uq_lut_board_type_name UNIQUE (board_name);


--
-- TOC entry 4951 (class 2606 OID 25443)
-- Name: lut_city uq_lut_city_name; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_city
    ADD CONSTRAINT uq_lut_city_name UNIQUE (cid, sid, did, city_name);


--
-- TOC entry 4939 (class 2606 OID 25400)
-- Name: lut_country uq_lut_country_cname; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_country
    ADD CONSTRAINT uq_lut_country_cname UNIQUE (cname);


--
-- TOC entry 4947 (class 2606 OID 25426)
-- Name: lut_district uq_lut_district_name; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_district
    ADD CONSTRAINT uq_lut_district_name UNIQUE (cid, sid, dname);


--
-- TOC entry 4977 (class 2606 OID 25645)
-- Name: lut_roles uq_lut_roles_code; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_roles
    ADD CONSTRAINT uq_lut_roles_code UNIQUE (role_code);


--
-- TOC entry 4979 (class 2606 OID 25643)
-- Name: lut_roles uq_lut_roles_name; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_roles
    ADD CONSTRAINT uq_lut_roles_name UNIQUE (role_name);


--
-- TOC entry 4971 (class 2606 OID 25524)
-- Name: lut_school_level uq_lut_school_level_code; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_school_level
    ADD CONSTRAINT uq_lut_school_level_code UNIQUE (school_level_code);


--
-- TOC entry 4973 (class 2606 OID 25526)
-- Name: lut_school_level uq_lut_school_level_name; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_school_level
    ADD CONSTRAINT uq_lut_school_level_name UNIQUE (school_level_name);


--
-- TOC entry 4965 (class 2606 OID 25503)
-- Name: lut_school_type uq_lut_school_type_code; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_school_type
    ADD CONSTRAINT uq_lut_school_type_code UNIQUE (school_type_code);


--
-- TOC entry 4967 (class 2606 OID 25505)
-- Name: lut_school_type uq_lut_school_type_name; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_school_type
    ADD CONSTRAINT uq_lut_school_type_name UNIQUE (school_type_name);


--
-- TOC entry 4943 (class 2606 OID 25410)
-- Name: lut_state uq_lut_state_name; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_state
    ADD CONSTRAINT uq_lut_state_name UNIQUE (cid, sname);


--
-- TOC entry 4955 (class 2606 OID 25463)
-- Name: lut_status uq_lut_status; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_status
    ADD CONSTRAINT uq_lut_status UNIQUE (sname, stype);


--
-- TOC entry 5001 (class 2606 OID 27279)
-- Name: tb_schools uq_tb_schools_gstin; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT uq_tb_schools_gstin UNIQUE (school_gstin);


--
-- TOC entry 5003 (class 2606 OID 27281)
-- Name: tb_schools uq_tb_schools_pan; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT uq_tb_schools_pan UNIQUE (school_pan);


--
-- TOC entry 5005 (class 2606 OID 27277)
-- Name: tb_schools uq_tb_schools_school_code; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT uq_tb_schools_school_code UNIQUE (school_code);


--
-- TOC entry 4997 (class 2606 OID 25693)
-- Name: tb_user_roles uq_tb_user_roles; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_user_roles
    ADD CONSTRAINT uq_tb_user_roles UNIQUE (user_id, role_id);


--
-- TOC entry 5006 (class 1259 OID 27376)
-- Name: idx_tb_school_contacts_contact_type; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_school_contacts_contact_type ON public.tb_school_contacts USING btree (contact_type);


--
-- TOC entry 5007 (class 1259 OID 27377)
-- Name: idx_tb_school_contacts_email; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_school_contacts_email ON public.tb_school_contacts USING btree (email);


--
-- TOC entry 5008 (class 1259 OID 27380)
-- Name: idx_tb_school_contacts_location; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_school_contacts_location ON public.tb_school_contacts USING btree (country_id, state_id, district_id, city_id);


--
-- TOC entry 5009 (class 1259 OID 27378)
-- Name: idx_tb_school_contacts_mobile; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_school_contacts_mobile ON public.tb_school_contacts USING btree (mobile_number);


--
-- TOC entry 5010 (class 1259 OID 27375)
-- Name: idx_tb_school_contacts_school_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_school_contacts_school_id ON public.tb_school_contacts USING btree (school_id);


--
-- TOC entry 5011 (class 1259 OID 27379)
-- Name: idx_tb_school_contacts_status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_school_contacts_status ON public.tb_school_contacts USING btree (status_id);


--
-- TOC entry 4992 (class 1259 OID 25714)
-- Name: idx_tb_user_roles_role_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_user_roles_role_id ON public.tb_user_roles USING btree (role_id);


--
-- TOC entry 4993 (class 1259 OID 25713)
-- Name: idx_tb_user_roles_user_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_user_roles_user_id ON public.tb_user_roles USING btree (user_id);


--
-- TOC entry 4980 (class 1259 OID 25708)
-- Name: idx_tb_users_email; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_users_email ON public.tb_users USING btree (email);


--
-- TOC entry 4981 (class 1259 OID 25709)
-- Name: idx_tb_users_mobile_number; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_users_mobile_number ON public.tb_users USING btree (mobile_number);


--
-- TOC entry 4982 (class 1259 OID 25706)
-- Name: idx_tb_users_org_user_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_users_org_user_id ON public.tb_users USING btree (org_user_id);


--
-- TOC entry 4983 (class 1259 OID 25704)
-- Name: idx_tb_users_school_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_users_school_id ON public.tb_users USING btree (school_id);


--
-- TOC entry 4984 (class 1259 OID 25710)
-- Name: idx_tb_users_status_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_users_status_id ON public.tb_users USING btree (status_id);


--
-- TOC entry 4985 (class 1259 OID 25705)
-- Name: idx_tb_users_user_type; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_users_user_type ON public.tb_users USING btree (user_type);


--
-- TOC entry 4986 (class 1259 OID 25707)
-- Name: idx_tb_users_username; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tb_users_username ON public.tb_users USING btree (username);


--
-- TOC entry 4989 (class 1259 OID 25711)
-- Name: uq_tb_users_school_org_user; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX uq_tb_users_school_org_user ON public.tb_users USING btree (school_id, org_user_id) WHERE ((school_id IS NOT NULL) AND (org_user_id IS NOT NULL));


--
-- TOC entry 4990 (class 1259 OID 25712)
-- Name: uq_tb_users_superadmin_org_user; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX uq_tb_users_superadmin_org_user ON public.tb_users USING btree (org_user_id) WHERE ((school_id IS NULL) AND (org_user_id IS NOT NULL));


--
-- TOC entry 4991 (class 1259 OID 27382)
-- Name: uq_tb_users_username; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX uq_tb_users_username ON public.tb_users USING btree (username);


--
-- TOC entry 5016 (class 2606 OID 25444)
-- Name: lut_city fk_lut_city_district; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_city
    ADD CONSTRAINT fk_lut_city_district FOREIGN KEY (cid, sid, did) REFERENCES public.lut_district(cid, sid, did);


--
-- TOC entry 5015 (class 2606 OID 25427)
-- Name: lut_district fk_lut_district_state; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_district
    ADD CONSTRAINT fk_lut_district_state FOREIGN KEY (cid, sid) REFERENCES public.lut_state(cid, sid);


--
-- TOC entry 5017 (class 2606 OID 25646)
-- Name: lut_roles fk_lut_roles_status; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_roles
    ADD CONSTRAINT fk_lut_roles_status FOREIGN KEY (status_id) REFERENCES public.lut_status(sid);


--
-- TOC entry 5014 (class 2606 OID 25411)
-- Name: lut_state fk_lut_state_country; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lut_state
    ADD CONSTRAINT fk_lut_state_country FOREIGN KEY (cid) REFERENCES public.lut_country(cid);


--
-- TOC entry 5030 (class 2606 OID 27365)
-- Name: tb_school_contacts fk_tb_school_contacts_city; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_school_contacts
    ADD CONSTRAINT fk_tb_school_contacts_city FOREIGN KEY (country_id, state_id, district_id, city_id) REFERENCES public.lut_city(cid, sid, did, city_id);


--
-- TOC entry 5031 (class 2606 OID 27350)
-- Name: tb_school_contacts fk_tb_school_contacts_country; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_school_contacts
    ADD CONSTRAINT fk_tb_school_contacts_country FOREIGN KEY (country_id) REFERENCES public.lut_country(cid);


--
-- TOC entry 5032 (class 2606 OID 27360)
-- Name: tb_school_contacts fk_tb_school_contacts_district; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_school_contacts
    ADD CONSTRAINT fk_tb_school_contacts_district FOREIGN KEY (country_id, state_id, district_id) REFERENCES public.lut_district(cid, sid, did);


--
-- TOC entry 5033 (class 2606 OID 27345)
-- Name: tb_school_contacts fk_tb_school_contacts_school; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_school_contacts
    ADD CONSTRAINT fk_tb_school_contacts_school FOREIGN KEY (school_id) REFERENCES public.tb_schools(school_id) ON DELETE CASCADE;


--
-- TOC entry 5034 (class 2606 OID 27355)
-- Name: tb_school_contacts fk_tb_school_contacts_state; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_school_contacts
    ADD CONSTRAINT fk_tb_school_contacts_state FOREIGN KEY (country_id, state_id) REFERENCES public.lut_state(cid, sid);


--
-- TOC entry 5035 (class 2606 OID 27370)
-- Name: tb_school_contacts fk_tb_school_contacts_status; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_school_contacts
    ADD CONSTRAINT fk_tb_school_contacts_status FOREIGN KEY (status_id) REFERENCES public.lut_status(sid);


--
-- TOC entry 5021 (class 2606 OID 27292)
-- Name: tb_schools fk_tb_schools_board_type; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT fk_tb_schools_board_type FOREIGN KEY (board_type_id) REFERENCES public.lut_board_type(board_type_id);


--
-- TOC entry 5022 (class 2606 OID 27312)
-- Name: tb_schools fk_tb_schools_city; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT fk_tb_schools_city FOREIGN KEY (country_id, state_id, district_id, city_id) REFERENCES public.lut_city(cid, sid, did, city_id);


--
-- TOC entry 5023 (class 2606 OID 27297)
-- Name: tb_schools fk_tb_schools_country; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT fk_tb_schools_country FOREIGN KEY (country_id) REFERENCES public.lut_country(cid);


--
-- TOC entry 5024 (class 2606 OID 27307)
-- Name: tb_schools fk_tb_schools_district; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT fk_tb_schools_district FOREIGN KEY (country_id, state_id, district_id) REFERENCES public.lut_district(cid, sid, did);


--
-- TOC entry 5025 (class 2606 OID 27287)
-- Name: tb_schools fk_tb_schools_school_level; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT fk_tb_schools_school_level FOREIGN KEY (school_level_id) REFERENCES public.lut_school_level(school_level_id);


--
-- TOC entry 5026 (class 2606 OID 27322)
-- Name: tb_schools fk_tb_schools_school_status; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT fk_tb_schools_school_status FOREIGN KEY (school_status_id) REFERENCES public.lut_status(sid);


--
-- TOC entry 5027 (class 2606 OID 27282)
-- Name: tb_schools fk_tb_schools_school_type; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT fk_tb_schools_school_type FOREIGN KEY (school_type_id) REFERENCES public.lut_school_type(school_type_id);


--
-- TOC entry 5028 (class 2606 OID 27302)
-- Name: tb_schools fk_tb_schools_state; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT fk_tb_schools_state FOREIGN KEY (country_id, state_id) REFERENCES public.lut_state(cid, sid);


--
-- TOC entry 5029 (class 2606 OID 27317)
-- Name: tb_schools fk_tb_schools_subscription_status; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_schools
    ADD CONSTRAINT fk_tb_schools_subscription_status FOREIGN KEY (subscription_status_id) REFERENCES public.lut_status(sid);


--
-- TOC entry 5019 (class 2606 OID 25699)
-- Name: tb_user_roles fk_tb_user_roles_role; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_user_roles
    ADD CONSTRAINT fk_tb_user_roles_role FOREIGN KEY (role_id) REFERENCES public.lut_roles(role_id) ON DELETE CASCADE;


--
-- TOC entry 5020 (class 2606 OID 25694)
-- Name: tb_user_roles fk_tb_user_roles_user; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_user_roles
    ADD CONSTRAINT fk_tb_user_roles_user FOREIGN KEY (user_id) REFERENCES public.tb_users(user_id) ON DELETE CASCADE;


--
-- TOC entry 5018 (class 2606 OID 25676)
-- Name: tb_users fk_tb_users_status; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tb_users
    ADD CONSTRAINT fk_tb_users_status FOREIGN KEY (status_id) REFERENCES public.lut_status(sid);


-- Completed on 2026-10-03 14:49:10

--
-- PostgreSQL database dump complete
--

\unrestrict 3Kd36I6mnpxXOoD2hfNIdg2ZZLzjtXuOqvyiYgCofyGSwAsmb5BWtEhs2e1rhud

